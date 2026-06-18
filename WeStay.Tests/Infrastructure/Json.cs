namespace WeStay.Tests.Infrastructure
{
    public static class Json
    {
        public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            var value = JsonSerializer.Deserialize<T>(body, Options);
            Assert.NotNull(value);
            return value!;
        }

        public static async Task<JsonElement> ReadElementAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(body, Options);
        }

        /// <summary>Case-insensitive check that a JSON object contains a property by name.</summary>
        public static bool HasProperty(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
