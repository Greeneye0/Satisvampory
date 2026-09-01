using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Satisvampory.Services
{
    internal static class EmbeddedJson
    {
        public static Dictionary<TKey, TValue> Load<TKey, TValue>(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Console.WriteLine("Resource not found!");
                return new Dictionary<TKey, TValue>();
            }

            using var reader = new StreamReader(stream);
            var parsed = JsonSerializer.Deserialize<Dictionary<TKey, TValue>>(reader.ReadToEnd());
            return parsed ?? new Dictionary<TKey, TValue>();
        }
    }
}
