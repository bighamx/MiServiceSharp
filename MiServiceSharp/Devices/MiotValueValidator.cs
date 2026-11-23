using System.Text.Json;
using System.Text.Json.Nodes;
using MiServiceSharp.Models;

namespace MiServiceSharp.Devices;

internal static class MiotValueValidator
{
    public static void Validate(MiotSpecProperty property, JsonNode? value)
    {
        var kind = value?.GetValueKind() ?? JsonValueKind.Null;
        var validType = property.Format switch
        {
            "bool" => kind is JsonValueKind.True or JsonValueKind.False,
            "string" => kind == JsonValueKind.String,
            "array" => kind == JsonValueKind.Array,
            "object" => kind == JsonValueKind.Object,
            "uint8" or "uint16" or "uint32" or "uint64" or "int8" or "int16" or "int32" or "int64" or "float" or "double" => kind == JsonValueKind.Number,
            _ => true
        };
        if (!validType)
        {
            throw new ArgumentException($"Property {property.Name} requires {property.Format}.", nameof(value));
        }

        if (kind == JsonValueKind.Number && property.Format is "float" or "double")
        {
            ValidateFloatingPoint(property, JsonSerializer.SerializeToElement(value).GetDouble());
        }
        else if (kind == JsonValueKind.Number)
        {
            var element = JsonSerializer.SerializeToElement(value);
            if (!element.TryGetDecimal(out var number))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Value exceeds the supported numeric range.");
            }
            var (minimum, maximum) = property.Format switch
            {
                "uint8" => (0m, 255m),
                "uint16" => (0m, 65535m),
                "uint32" => (0m, uint.MaxValue),
                "uint64" => (0m, ulong.MaxValue),
                "int8" => (-128m, 127m),
                "int16" => (short.MinValue, (decimal)short.MaxValue),
                "int32" => (int.MinValue, (decimal)int.MaxValue),
                "int64" => (long.MinValue, (decimal)long.MaxValue),
                _ => (decimal.MinValue, decimal.MaxValue)
            };
            if (number < minimum || number > maximum
                || (property.Format.Contains("int", StringComparison.Ordinal) && decimal.Truncate(number) != number))
            {
                throw new ArgumentOutOfRangeException(nameof(value), $"Value is invalid for {property.Format}.");
            }
            if (property.ValueRange.Count >= 2)
            {
                var rangeMinimum = property.ValueRange[0].GetDecimal();
                var rangeMaximum = property.ValueRange[1].GetDecimal();
                var step = property.ValueRange.Count > 2 ? property.ValueRange[2].GetDecimal() : 0;
                if (number < rangeMinimum || number > rangeMaximum || (step > 0 && (number - rangeMinimum) % step != 0))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), $"Value does not match the range or step for {property.Name}.");
                }
            }
        }

        if (property.ValueList.Count > 0
            && !property.ValueList.Any(item => JsonNode.DeepEquals(JsonNode.Parse(item.Value.GetRawText()), value)))
        {
            throw new ArgumentException($"Value is not in the allowed values for {property.Name}.", nameof(value));
        }
    }

    private static void ValidateFloatingPoint(MiotSpecProperty property, double number)
    {
        if (!double.IsFinite(number) || (property.Format == "float" && Math.Abs(number) > float.MaxValue))
        {
            throw new ArgumentOutOfRangeException(nameof(number), $"Value is invalid for {property.Format}.");
        }
        if (property.ValueRange.Count < 2)
        {
            return;
        }
        var minimum = property.ValueRange[0].GetDouble();
        var maximum = property.ValueRange[1].GetDouble();
        if (number < minimum || number > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(number), $"Value does not match the range for {property.Name}.");
        }
        if (property.ValueRange.Count > 2 && property.ValueRange[2].GetDouble() is var step && step > 0)
        {
            var steps = (number - minimum) / step;
            if (Math.Abs(steps - Math.Round(steps)) > 1e-6 * Math.Max(1, Math.Abs(steps)))
            {
                throw new ArgumentOutOfRangeException(nameof(number), $"Value does not match the step for {property.Name}.");
            }
        }
    }
}
