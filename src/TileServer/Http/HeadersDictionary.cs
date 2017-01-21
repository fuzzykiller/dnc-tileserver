using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TileServer.Http
{
    internal class HeadersDictionary : IDictionary<string, string>, IReadOnlyDictionary<string, string>
    {
        private readonly Dictionary<string, string> _backend =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // TODO: Make dedicated storage or whatever to avoid needlessly converting
        public int? ContentLength
        {
            get
            {
                string temp;
                int result;
                return _backend.TryGetValue(WellKnownHeaders.ContentLength, out temp) &&
                       int.TryParse(temp, NumberStyles.None, CultureInfo.InvariantCulture, out result)
                    ? result
                    : default(int?);
            }

            set
            {
                if (value == null)
                {
                    _backend.Remove(WellKnownHeaders.ContentLength);
                }
                else
                {
                    _backend[WellKnownHeaders.ContentLength] = value.Value.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        public void Freeze()
        {
            IsReadOnly = true;
        }

        public override string ToString()
        {
            return string.Join("\r\n", this.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
        }

        public void Clear()
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException("Dictionary is frozen and read-only.");
            }

            _backend.Clear();
        }

        public int Count => _backend.Count;

        public bool IsReadOnly { get; private set; }

        public void Add(string key, string value)
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException("Dictionary is frozen and read-only.");
            }

            if (key.IndexOf(':') != -1)
            {
                throw new ArgumentException("Colon not allowed in header name", nameof(key));
            }

            _backend.Add(key, value);
        }

        public bool ContainsKey(string key)
        {
            return _backend.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException("Dictionary is frozen and read-only.");
            }

            return _backend.Remove(key);
        }

        public bool TryGetValue(string key, out string value)
        {
            return _backend.TryGetValue(key, out value);
        }

        public string this[string key]
        {
            get { return _backend[key]; }
            set
            {
                if (IsReadOnly)
                {
                    throw new InvalidOperationException("Dictionary is frozen and read-only.");
                }

                _backend[key] = value;
            }
        }

        public ICollection<string> Keys => _backend.Keys;
        public ICollection<string> Values => _backend.Values;

        #region IEnumerable

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return _backend.GetEnumerator();
        }

        #endregion

        #region ICollection

        void ICollection<KeyValuePair<string, string>>.Add(KeyValuePair<string, string> item)
        {
            Add(item.Key, item.Value);
        }

        bool ICollection<KeyValuePair<string, string>>.Contains(KeyValuePair<string, string> item)
        {
            return ((ICollection<KeyValuePair<string, string>>)_backend).Contains(item);
        }

        void ICollection<KeyValuePair<string, string>>.CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<string, string>>)_backend).CopyTo(array, arrayIndex);
        }

        bool ICollection<KeyValuePair<string, string>>.Remove(KeyValuePair<string, string> item)
        {
            return ((ICollection<KeyValuePair<string, string>>)_backend).Remove(item);
        }

        #endregion

        #region IReadOnlyDictionary
        
        IEnumerable<string> IReadOnlyDictionary<string, string>.Keys => Keys;

        IEnumerable<string> IReadOnlyDictionary<string, string>.Values => Values;
        
        #endregion
    }
}