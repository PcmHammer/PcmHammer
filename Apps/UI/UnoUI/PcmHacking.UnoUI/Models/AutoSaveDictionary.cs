using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Windows.Foundation.Collections;

namespace PcmHacking.UnoUI.Models
{
    public class AutoSaveDictionary<K, V> : IPropertySet, IObservableMap<string, object>, IDictionary<string, object>, ICollection<KeyValuePair<string, object>>, IEnumerable<KeyValuePair<string, object>>, IEnumerable
    {
        private readonly Dictionary<string, object?> _storageContainer;

        public AutoSaveDictionary()
        {
            _storageContainer = [];
        }

        public object this[string key]
        {
            get
            {
                if (!_storageContainer.ContainsKey(key))
                {
                    _storageContainer.Add(key, null);
                }
                if(_storageContainer[key] is JsonElement)
                {
                    JsonElement element = (JsonElement)_storageContainer[key];
                    try
                    {
                        return element.GetBoolean();
                    }
                    catch
                    {
                        return element.GetString();
                    }
                }
                return _storageContainer[key];
            }
            set
            {
                _storageContainer[key] = value;
                try
                {
                    FileInfo loadedExe = new(Assembly.GetExecutingAssembly().Location);
                    string cfgPath = $@"{loadedExe.Directory.FullName}\Settings.Windows.json";
                    string contents = JsonSerializer.Serialize(_storageContainer);
                    if (!string.IsNullOrEmpty(contents))
                    {
                        File.WriteAllText(cfgPath, contents);
                    }
                }
                catch { }
            }
        }

        public ICollection<string> Keys => _storageContainer.Keys;
        public ICollection<object> Values => _storageContainer.Values;

        public int Count => _storageContainer.Count;

        public bool IsReadOnly => false;

        public event MapChangedEventHandler<string, object> MapChanged;

        public void Add(string key, object value)
        {
            _storageContainer.Add(key, value);
        }

        public void Add(KeyValuePair<string, object> item)
        {
            _storageContainer.Add(item.Key, item.Value);
        }

        public void Clear()
        {
            return;
        }

        public bool Contains(KeyValuePair<string, object> item)
        {
            return _storageContainer.ContainsKey(item.Key) && _storageContainer[item.Key] == item.Value;
        }

        public bool ContainsKey(string key)
        {
            return _storageContainer.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return _storageContainer.GetEnumerator();
        }

        public bool Remove(string key)
        {
            return _storageContainer.Remove(key);
        }

        public bool Remove(KeyValuePair<string, object> item)
        {
            return _storageContainer.Remove(item.Key);
        }

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out object value)
        {
            return _storageContainer.TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
