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
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly object _gate = new();
    private readonly Dictionary<string, Seat> _seats = new();
    private string? _code;
    private DateTime _codeUntil;

    public string StartCode()
    {
        lock (_gate)
        {
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
            var seat = SeatFor(clientId);
            seat.Owner = false;
            seat.Helper = true;
            seat.Control = false;
            seat.Asking = false;
            return HelpJoin.Ok;
        }
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
            var id = HelperIdLocked();
            if (id != null) _seats.Remove(id);
            return id;
        }
    }

    /// <summary>Drops any seat. Returns true when the disconnected client was the helper.</summary>
    public bool Remove(string clientId)
    {
        lock (_gate)
        {
            if (!_seats.TryGetValue(clientId, out var seat)) return false;
            var helper = seat.Helper;
            _seats.Remove(clientId);
            return helper;
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

    private sealed class Seat
    {
        public bool Owner;
        public bool Helper;
        public bool Control;
        public bool Asking;
        public DateTime ControlUntil;
    }
}

public enum HelpJoin
{
    Ok,
    Fail,
    Expired,
    Busy,
}
