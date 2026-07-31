using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;

namespace FastFluentFilesFolders.Services
{
    public class IconCache
    {
        private readonly Dictionary<string, ImageSource> _cache = new();
        private readonly LinkedList<string> _accessOrder = new();
        private readonly int _maxCapacity;
        private readonly object _lock = new();

        public int Count
        {
            get { lock (_lock) { return _cache.Count; } }
        }

        public IconCache(int maxCapacity = 1500)
        {
            _maxCapacity = maxCapacity;
        }

        public bool TryGet(string key, out ImageSource? icon)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out icon))
                {
                    _accessOrder.Remove(key);
                    _accessOrder.AddLast(key);
                    return true;
                }
                return false;
            }
        }

        public void Set(string key, ImageSource icon)
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(key))
                {
                    _cache[key] = icon;
                    _accessOrder.Remove(key);
                    _accessOrder.AddLast(key);
                    return;
                }

                while (_cache.Count >= _maxCapacity)
                {
                    var oldest = _accessOrder.First!.Value;
                    _accessOrder.RemoveFirst();
                    _cache.Remove(oldest);
                }

                _cache[key] = icon;
                _accessOrder.AddLast(key);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
                _accessOrder.Clear();
            }
        }
    }
}
