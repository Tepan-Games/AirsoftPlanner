using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AirsoftPlanner.Data;

internal class DateTimeOffsetToUtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
    value => value.UtcTicks,
    ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
