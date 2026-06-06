using System;
using System.Collections.Generic;

namespace TbhDpsMeter
{
    /// <summary>
    /// Pure, Unity-independent accumulator for damage the player's heroes TAKE.
    /// Mirrors <see cref="DpsTracker"/> (sliding-window live rate, peak, total,
    /// duration, hits) and adds damage-taken specifics: biggest single hit, the
    /// incoming (monster) crit rate, and a second breakdown by element attribute.
    /// All time values are seconds supplied by the caller, so it unit-tests with a
    /// fake clock. Single-threaded: called from the game's main thread only.
    /// </summary>
    public sealed class DamageTakenTracker
    {
        // EDamageAttribute values from the game (sequential, single-select).
        public static readonly (int value, string name)[] AttributeOrder = new[]
        {
            (0, "Physical"),
            (1, "Fire"),
            (2, "Cold"),
            (3, "Lightning"),
            (4, "Chaos"),
            (5, "AllElement"),
            (6, "None"),
        };

        private readonly float _windowSeconds;
        private readonly Queue<(float t, float dmg)> _window = new Queue<(float, float)>();

        private float _startTime;
        private float _lastDamageTime;
        private bool _active;

        private double _total;
        private long _hits;
        private long _crits;          // incoming hits flagged IsCritical (monster crits)
        private float _biggestHit;
        private float _peakDtps;
        private readonly Dictionary<int, double> _byType = new Dictionary<int, double>();
        private readonly Dictionary<int, double> _byAttribute = new Dictionary<int, double>();

        public DamageTakenTracker(float windowSeconds = 5f)
        {
            _windowSeconds = windowSeconds <= 0 ? 5f : windowSeconds;
        }

        public bool HasData => _hits > 0;
        public float LastDamageTime => _lastDamageTime;

        /// <summary>Begin a fresh encounter; clears all accumulated stats.</summary>
        public void StartEncounter(float now)
        {
            _window.Clear();
            _byType.Clear();
            _byAttribute.Clear();
            _startTime = now;
            _lastDamageTime = now;
            _active = true;
            _total = 0;
            _hits = 0;
            _crits = 0;
            _biggestHit = 0;
            _peakDtps = 0;
        }

        /// <summary>Stop accumulating but keep the numbers for display.</summary>
        public void EndEncounter() => _active = false;

        /// <summary>Record one damage instance the player side took.</summary>
        public void Record(float amount, bool isCritical, int damageTypeFlag, int attributeValue, float now)
        {
            if (amount <= 0) return;
            if (!_active) StartEncounter(now);

            _total += amount;
            _hits++;
            if (isCritical) _crits++;
            if (amount > _biggestHit) _biggestHit = amount;

            if (_byType.TryGetValue(damageTypeFlag, out var ct)) _byType[damageTypeFlag] = ct + amount;
            else _byType[damageTypeFlag] = amount;

            if (_byAttribute.TryGetValue(attributeValue, out var ca)) _byAttribute[attributeValue] = ca + amount;
            else _byAttribute[attributeValue] = amount;

            _lastDamageTime = now;
            _window.Enqueue((now, amount));
            Trim(now);

            float live = LiveDtps(now);
            if (live > _peakDtps) _peakDtps = live;
        }

        private void Trim(float now)
        {
            float cutoff = now - _windowSeconds;
            while (_window.Count > 0 && _window.Peek().t < cutoff)
                _window.Dequeue();
        }

        /// <summary>Instantaneous damage-taken-per-second over the trailing window.</summary>
        public float LiveDtps(float now)
        {
            Trim(now);
            double sum = 0;
            foreach (var e in _window) sum += e.dmg;
            float elapsed = now - _startTime;
            float divisor = elapsed < _windowSeconds ? elapsed : _windowSeconds;
            if (divisor < 1f) divisor = 1f;   // floor so the first hits don't divide by ~0
            return (float)(sum / divisor);
        }

        public Snapshot GetSnapshot(float now)
        {
            float duration = _hits > 0 ? Math.Max(0f, _lastDamageTime - _startTime) : 0f;
            float avg = duration > 0.001f ? (float)(_total / duration) : 0f;

            return new Snapshot
            {
                Active = _active,
                LiveDtps = LiveDtps(now),
                PeakDtps = _peakDtps,
                AvgDtps = avg,
                Total = _total,
                DurationSeconds = duration,
                Hits = _hits,
                CritRate = _hits > 0 ? (float)_crits / _hits : 0f,
                BiggestHit = _biggestHit,
                ByType = BuildParts(_byType, DpsTracker.DecodeName),
                ByAttribute = BuildParts(_byAttribute, DecodeAttribute),
            };
        }

        private List<Part> BuildParts(Dictionary<int, double> src, Func<int, string> namer)
        {
            var parts = new List<Part>();
            if (_total <= 0) return parts;
            foreach (var kv in src)
            {
                if (kv.Value <= 0) continue;
                parts.Add(new Part
                {
                    Key = kv.Key,
                    Name = namer(kv.Key),
                    Amount = kv.Value,
                    Share = (float)(kv.Value / _total),
                });
            }
            parts.Sort((x, y) => y.Amount.CompareTo(x.Amount)); // largest share first
            return parts;
        }

        /// <summary>Human-readable English name for an EDamageAttribute value.</summary>
        public static string DecodeAttribute(int value)
        {
            foreach (var (v, name) in AttributeOrder) if (v == value) return name;
            return "Attr" + value;
        }

        public struct Part
        {
            public int Key;        // type flag or attribute value
            public string Name;
            public double Amount;
            public float Share;
        }

        public struct Snapshot
        {
            public bool Active;
            public float LiveDtps;
            public float PeakDtps;
            public float AvgDtps;
            public double Total;
            public float DurationSeconds;
            public long Hits;
            public float CritRate;     // incoming crit rate
            public float BiggestHit;
            public List<Part> ByType;
            public List<Part> ByAttribute;
        }
    }
}
