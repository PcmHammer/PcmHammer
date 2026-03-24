using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Uno.Extensions.Specialized;
using Windows.Foundation.Collections;

namespace PcmHacking.UnoUI.Models
{
    public class AutoSaveDictionary : IPropertySet
    {
        private readonly Dictionary<string, object?> _storageContainer;

        // Setting `UnmappedMemberHandling` to `Disallow` will cause
        // the (De)Serializer to reject and throw an exception if an
        // attempt to load an incorrect object type occurs. Used in
        // the `TryDeserialize` method, as well as load and save.
        private static readonly JsonSerializerOptions _serializerOptions = new() 
        {
            WriteIndented = true, 
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        };

        public AutoSaveDictionary()
        {
            _storageContainer = [];
        }

        public static AutoSaveDictionary Load()
        {
            AutoSaveDictionary dictionary = new();
            try
            {
                string filePath = getFilePath();
                if (!File.Exists(filePath))
                {
                    using FileStream stream = File.Create(filePath);
                    return dictionary;
                }

                string contents = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(contents))
                {
                    return dictionary;
                }

                Dictionary<string, object>? persistedEntries = JsonSerializer.Deserialize<Dictionary<string, object>>(contents, _serializerOptions);
                if (persistedEntries is null)
                {
                    return dictionary;
                }
                dictionary.AddRange(persistedEntries);
                return dictionary;
            }
            catch
            {
                return dictionary;
            }
        }

        public object this[string key]
        {
            get
            {
                if (!_storageContainer.TryGetValue(key, out object? storedValue))
                {
                    _storageContainer[key] = null;
                    return null;
                }
                if (storedValue is JsonElement)
                {
                    JsonElement element = (JsonElement)storedValue;
                    switch (element.ValueKind)
                    {
                        case JsonValueKind.Undefined:
                        case JsonValueKind.Object: // Storing class objects in a JsonElement is possible, but will need manual testing to pull it back out.
                            string json = element.ToString();
                            if (TryDeserialize(json, out CurrentSettings settings))
                            {
                                return settings;
                            }
                            break;
                        case JsonValueKind.Null:
                            return null; // These cases should not happen.
                        case JsonValueKind.Array: // Arrays will need some manual handling.
                            var array = element.EnumerateArray().ToArray();
                            if (array.Length > 0)
                            {
                                switch (array[0].ValueKind) // This of course has potential for failure, 
                                {
                                    case JsonValueKind.String:
                                        return array.Select(x => x.ToString()).ToArray();
                                    case JsonValueKind.Number:
                                        return array.Select(x => x.GetInt32()).ToArray();
                                    case JsonValueKind.True:
                                    case JsonValueKind.False:
                                        return array.Select(x => x.GetBoolean()).ToArray();
                                }
                            }
                                break;
                        case JsonValueKind.String:
                            return element.GetString();
                        case JsonValueKind.Number:
                            return element.GetInt32();
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                            return element.GetBoolean();
                    }
                }
                if(storedValue == null || storedValue is string || storedValue is bool)
                {
                    return storedValue;
                }
                return null; // At this point, if it's not a simple string or boolean type, we have a problem...
            }
            set
            {
                _storageContainer[key] = value;
                Persist();
            }
        }

        private bool TryDeserialize<T>(string jsonString, out T outputObject)
        {
            try
            {
                outputObject = JsonSerializer.Deserialize<T>(jsonString, _serializerOptions);
            } catch (Exception)
            {
                outputObject = default(T);
                return false;
            }
            return true;
        }

        public ICollection<string> Keys => _storageContainer.Keys;
        public ICollection<object?> Values => _storageContainer.Values;

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
            if (!_storageContainer.TryGetValue(item.Key, out object? storedValue))
            {
                return false;
            }

            return Equals(storedValue, item.Value);
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
            foreach (KeyValuePair<string, object?> entry in _storageContainer)
            {
                yield return new KeyValuePair<string, object>(entry.Key, entry.Value!);
            }
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
            if (_storageContainer.TryGetValue(key, out object? storedValue))
            {
                value = storedValue;
                return true;
            }

            value = null;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void Persist()
        {
            try
            {
                string filePath = AutoSaveDictionary.getFilePath();
                string contents = JsonSerializer.Serialize(_storageContainer, _serializerOptions);
                if (!string.IsNullOrEmpty(contents))
                {
                    File.WriteAllText(filePath, contents);
                }
            }
            catch
            {
            }
        }

        private static string getFilePath()
        {
            FileInfo loadedExe = new(Assembly.GetExecutingAssembly().Location);
            return $@"{loadedExe.Directory.FullName}\Settings.Windows.json";
        }
    }
}
