using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Windows.Foundation.Collections;

namespace PcmHacking.UnoUI.Models
{
    public class AutoSaveDictionary : IPropertySet
    {
        private class TypeAndValue
        {
            public string? Type { get; set; }
            public JsonElement? Value { get; set; }

            public TypeAndValue()
            {
            }

            public TypeAndValue(string? type, JsonElement? value)
            {
                Type = type;
                Value = value;
            }
        }

        private readonly Dictionary<string, object?> _storageContainer;

        public AutoSaveDictionary()
        {
            _storageContainer = [];
        }

        public static AutoSaveDictionary Load()
        {
            AutoSaveDictionary dictionary = new();
            try
            {
                string filePath = AutoSaveDictionary.getFilePath();
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

                Dictionary<string, JsonElement>? persistedEntries = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(contents);
                if (persistedEntries is null)
                {
                    return dictionary;
                }

                foreach (KeyValuePair<string, JsonElement> entry in persistedEntries)
                {
                    TypeAndValue? wrapper = ConvertJsonElementToWrapper(entry.Value);
                    if (wrapper is not null)
                    {
                        dictionary._storageContainer[entry.Key] = wrapper;
                        continue;
                    }

                    object? materialized = entry.Value.Deserialize<object?>();
                    dictionary._storageContainer[entry.Key] = CreateTypeAnnotatedValue(materialized);
                }

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

                return ExtractValue(key, storedValue);
            }
            set
            {
                _storageContainer[key] = CreateTypeAnnotatedValue(value);
                try
                {
                    string filePath = AutoSaveDictionary.getFilePath();
                    string contents = JsonSerializer.Serialize(_storageContainer);
                    if (!string.IsNullOrEmpty(contents))
                    {
                        File.WriteAllText(filePath, contents);
                    }
                }
                catch { }
            }
        }

        public ICollection<string> Keys => _storageContainer.Keys;
        public ICollection<object?> Values
        {
            get
            {
                List<object?> values = new(_storageContainer.Count);
                foreach (KeyValuePair<string, object?> entry in _storageContainer)
                {
                    values.Add(DeserializeStoredValue(entry.Value));
                }

                return values;
            }
        }

        public int Count => _storageContainer.Count;

        public bool IsReadOnly => false;

        public event MapChangedEventHandler<string, object> MapChanged;

        public void Add(string key, object value)
        {
            _storageContainer.Add(key, CreateTypeAnnotatedValue(value));
        }

        public void Add(KeyValuePair<string, object> item)
        {
            _storageContainer.Add(item.Key, CreateTypeAnnotatedValue(item.Value));
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

            object? currentValue = ExtractValue(item.Key, storedValue);
            return Equals(currentValue, item.Value);
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
                yield return new KeyValuePair<string, object>(entry.Key, DeserializeStoredValue(entry.Value)!);
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
                value = ExtractValue(key, storedValue);
                return true;
            }

            value = null;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private static TypeAndValue CreateTypeAnnotatedValue(object? value)
        {
            if (value is TypeAndValue wrapper)
            {
                return wrapper;
            }

            if (value is JsonElement jsonElement)
            {
                TypeAndValue? elementWrapper = ConvertJsonElementToWrapper(jsonElement);
                if (elementWrapper is not null)
                {
                    return elementWrapper;
                }

                object? materialized = jsonElement.Deserialize<object?>();
                return CreateTypeAnnotatedValue(materialized);
            }

            Type actualType = value?.GetType() ?? typeof(object);
            JsonElement serializedValue = JsonSerializer.SerializeToElement(value, actualType);
            return new TypeAndValue(GetTypeIdentifier(actualType), serializedValue);
        }

        private static object? DeserializeStoredValue(object? storedValue)
        {
            return storedValue switch
            {
                TypeAndValue wrapper => DeserializeWrapper(wrapper),
                JsonElement jsonElement => ConvertJsonElementToWrapper(jsonElement) is { } wrapper
                    ? DeserializeWrapper(wrapper)
                    : jsonElement.Deserialize<object?>(),
                _ => storedValue
            };
        }

        private object? ExtractValue(string key, object? storedValue)
        {
            TypeAndValue wrapper = storedValue switch
            {
                TypeAndValue existing => existing,
                JsonElement jsonElement => ConvertJsonElementToWrapper(jsonElement) ?? CreateTypeAnnotatedValue(jsonElement.Deserialize<object?>()),
                _ => CreateTypeAnnotatedValue(storedValue)
            };

            _storageContainer[key] = wrapper;
            return DeserializeWrapper(wrapper);
        }

        private static TypeAndValue? ConvertJsonElementToWrapper(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? typeName = null;
            JsonElement? valueElement = null;

            if (element.TryGetProperty(nameof(TypeAndValue.Type), out JsonElement typeProperty))
            {
                typeName = typeProperty.GetString();
            }

            if (element.TryGetProperty(nameof(TypeAndValue.Value), out JsonElement valueProperty))
            {
                valueElement = valueProperty;
            }

            if (typeName is null && valueElement is null)
            {
                return null;
            }

            return new TypeAndValue(typeName, valueElement);
        }

        private static object? DeserializeWrapper(TypeAndValue wrapper)
        {
            if (wrapper.Value is null)
            {
                return null;
            }

            Type? targetType = null;

            if (!string.IsNullOrWhiteSpace(wrapper.Type))
            {
                targetType = Type.GetType(wrapper.Type);
            }

            targetType ??= typeof(object);

            try
            {
                return JsonSerializer.Deserialize(wrapper.Value.Value, targetType);
            }
            catch
            {
                return wrapper.Value.Value.Deserialize<object?>();
            }
        }

        private static string GetTypeIdentifier(Type type)
        {
            return type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        }

        private static string getFilePath()
        {
            FileInfo loadedExe = new(Assembly.GetExecutingAssembly().Location);
            return $@"{loadedExe.Directory.FullName}\Settings.Windows.json";
        }
    }
}
