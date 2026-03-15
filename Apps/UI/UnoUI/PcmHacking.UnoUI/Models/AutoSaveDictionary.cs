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
                    dictionary._storageContainer[entry.Key] = DeserializePersistedEntry(entry.Value);
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

                return storedValue;
            }
            set
            {
                _storageContainer[key] = value;
                Persist();
            }
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

        private static TypeAndValue CreateTypeAnnotatedValue(object? value)
        {
            if (value is TypeAndValue wrapper)
            {
                return wrapper;
            }

            Type actualType = value?.GetType() ?? typeof(object);
            JsonElement serializedValue = value is JsonElement jsonElement
                ? jsonElement
                : JsonSerializer.SerializeToElement(value, actualType);

            return new TypeAndValue(GetTypeIdentifier(actualType), serializedValue);
        }

        private void Persist()
        {
            try
            {
                string filePath = AutoSaveDictionary.getFilePath();
                Dictionary<string, TypeAndValue> persistedEntries = new(_storageContainer.Count);

                foreach (KeyValuePair<string, object?> entry in _storageContainer)
                {
                    persistedEntries[entry.Key] = CreateTypeAnnotatedValue(entry.Value);
                }

                string contents = JsonSerializer.Serialize(persistedEntries);
                if (!string.IsNullOrEmpty(contents))
                {
                    File.WriteAllText(filePath, contents);
                }
            }
            catch
            {
            }
        }

        private static object? DeserializePersistedEntry(JsonElement element)
        {
            if (TryConvertJsonElementToWrapper(element, out TypeAndValue? wrapper))
            {
                return DeserializeWrapper(wrapper);
            }

            try
            {
                return element.Deserialize<object?>();
            }
            catch
            {
                return null;
            }
        }

        private static bool TryConvertJsonElementToWrapper(JsonElement element, [NotNullWhen(true)] out TypeAndValue? typeAndValue)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                typeAndValue = null;
                return false;
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
                typeAndValue = null;
                return false;
            }

            typeAndValue = new TypeAndValue(typeName, valueElement);
            return true;
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
