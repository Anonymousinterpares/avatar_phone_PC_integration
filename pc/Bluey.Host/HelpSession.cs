using System.Security.Cryptography;

namespace Bluey.Host;

/// <summary>
/// One helper at a time. The code works once, for ten minutes.
/// Mouse control is a separate grant, also for ten minutes.
/// </summary>
public sealed class HelpSession
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ControlLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResumeLifetime = TimeSpan.FromMinutes(2);
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly object _gate = new();
    private readonly Dictionary<string, Seat> _seats = new();
    private string? _code;
    private DateTime _codeUntil;
    private Seat? _parked;
    private DateTime _parkedUntil;

    public string StartCode()
    {
        lock (_gate)
        {
            _parked = null;
            _code = NewCode();
            _codeUntil = DateTime.UtcNow.Add(CodeLifetime);
            return _code;
        }
    }

    public void CancelCode()
    {
        lock (_gate) _code = null;
    }

    public HelpJoin TryJoin(string clientId, string attempt)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return HelpJoin.Fail;
        lock (_gate)
        {
            if (HelperIdLocked() != null) return HelpJoin.Busy;
            if (string.IsNullOrEmpty(_code)) return HelpJoin.Fail;
            if (DateTime.UtcNow > _codeUntil)
            {
                _code = null;
                return HelpJoin.Expired;
            }
            if (!string.Equals(_code, attempt.Trim(), StringComparison.OrdinalIgnoreCase)) return HelpJoin.Fail;
            _code = null;
            _parked = null;
            var seat = SeatFor(clientId);
            seat.Owner = false;
            seat.Helper = true;
            seat.Control = false;
            seat.Asking = false;
            seat.Token = NewToken();
            return HelpJoin.Ok;
        }
    }

    public HelpJoin TryResume(string clientId, string token)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(token)) return HelpJoin.Fail;
        lock (_gate)
        {
            if (_parked == null || DateTime.UtcNow > _parkedUntil || !string.Equals(_parked.Token, token.Trim(), StringComparison.OrdinalIgnoreCase))
                return HelpJoin.Fail;
            if (HelperIdLocked() != null) return HelpJoin.Busy;
            _parked.Asking = false;
            _seats[clientId] = _parked;
            _parked = null;
            return HelpJoin.Ok;
        }
    }

    public string? ResumeToken(string clientId)
    {
        lock (_gate)
            return _seats.TryGetValue(clientId, out var seat) && seat.Helper ? seat.Token : null;
    }

    public void MarkOwner(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return;
        lock (_gate)
        {
            var seat = SeatFor(clientId);
            seat.Owner = true;
            seat.Helper = false;
            seat.Control = true;
            seat.Asking = false;
        }
    }

    public bool CanControl(string clientId)
    {
        lock (_gate)
        {
            if (!_seats.TryGetValue(clientId, out var seat)) return false;
            if (seat.Owner) return true;
            if (!seat.Helper || !seat.Control) return false;
            if (DateTime.UtcNow < seat.ControlUntil) return true;
            seat.Control = false;
            return false;
        }
    }

    public bool BeginAsk(string clientId)
    {
        lock (_gate)
        {
            if (!_seats.TryGetValue(clientId, out var seat) || !seat.Helper || seat.Control || seat.Asking) return false;
            seat.Asking = true;
            return true;
        }
    }

    public bool Grant(string clientId)
    {
        lock (_gate)
        {
            if (!_seats.TryGetValue(clientId, out var seat) || !seat.Helper) return false;
            seat.Asking = false;
            seat.Control = true;
            seat.ControlUntil = DateTime.UtcNow.Add(ControlLifetime);
            return true;
        }
    }

    public void ClearAsking(string clientId)
    {
        lock (_gate)
        {
            if (_seats.TryGetValue(clientId, out var seat)) seat.Asking = false;
        }
    }

    public bool HasAnyone()
    {
        lock (_gate) return _seats.Count > 0;
    }

    public bool IsHelper(string clientId)
    {
        lock (_gate) return _seats.TryGetValue(clientId, out var seat) && seat.Helper;
    }

    public string? RevokeHelperControl()
    {
        lock (_gate)
        {
            var id = HelperIdLocked();
            if (id == null || !_seats.TryGetValue(id, out var seat) || !seat.Control) return null;
            seat.Control = false;
            seat.Asking = false;
            return id;
        }
    }

    public string? ExpireControl()
    {
        lock (_gate)
        {
            var id = HelperIdLocked();
            if (id == null || !_seats.TryGetValue(id, out var seat) || !seat.Control) return null;
            if (DateTime.UtcNow < seat.ControlUntil) return null;
            seat.Control = false;
            seat.Asking = false;
            return id;
        }
    }

    /// <summary>Removes the helper and returns their client id when one was connected.</summary>
    public string? KickHelper()
    {
        lock (_gate)
        {
            _parked = null;
            var id = HelperIdLocked();
            if (id != null) _seats.Remove(id);
            return id;
        }
    }

    /// <summary>Drops any seat for good. Returns true when that client was the helper.</summary>
    public bool Remove(string clientId)
    {
        lock (_gate)
        {
            if (!_seats.TryGetValue(clientId, out var seat)) return false;
            _seats.Remove(clientId);
            if (seat.Helper) _parked = null;
            return seat.Helper;
        }
    }

    /// <summary>
    /// A dropped helper can reconnect with their token for two minutes.
    /// Returns true when the closed client was the helper.
    /// </summary>
    public bool Park(string clientId)
    {
        lock (_gate)
        {
            if (!_seats.TryGetValue(clientId, out var seat)) return false;
            _seats.Remove(clientId);
            if (!seat.Helper) return false;
            seat.Asking = false;
            _parked = seat;
            _parkedUntil = DateTime.UtcNow.Add(ResumeLifetime);
            return true;
        }
    }

    public bool ExpirePark()
    {
        lock (_gate)
        {
            if (_parked == null || DateTime.UtcNow <= _parkedUntil) return false;
            _parked = null;
            return true;
        }
    }

    private Seat SeatFor(string clientId)
    {
        if (!_seats.TryGetValue(clientId, out var seat))
        {
            seat = new Seat();
            _seats[clientId] = seat;
        }
        return seat;
    }

    private string? HelperIdLocked()
    {
        foreach (var pair in _seats)
        {
            if (pair.Value.Helper) return pair.Key;
        }
        return null;
    }

    private static string NewCode()
    {
        var bytes = new byte[6];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    private static string NewToken()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }

    private sealed class Seat
    {
        public bool Owner;
        public bool Helper;
        public bool Control;
        public bool Asking;
        public DateTime ControlUntil;
        public string Token = "";
    }
}

public enum HelpJoin
{
    Ok,
    Fail,
    Expired,
    Busy,
}
