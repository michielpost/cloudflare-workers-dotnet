namespace Shared;

// Records for the two Open-Meteo endpoints the telemetry pipeline calls. The
// names mirror the API's own snake_case fields because the compiler emits a
// member read as-is (`forecast.current.temperature_2m`), so they parse straight
// out of the response without a mapping step.

/// <summary>Answer of <c>/v1/forecast</c>.</summary>
public sealed record ForecastResponse(ForecastCurrent Current);

/// <summary>The <c>current</c> block of the forecast response.</summary>
public sealed record ForecastCurrent(double Temperature_2m, int Weather_code);

/// <summary>Answer of the <c>/v1/air-quality</c> endpoint.</summary>
public sealed record AirQualityResponse(AirQualityCurrent Current);

/// <summary>The <c>current</c> block of the air quality response.</summary>
public sealed record AirQualityCurrent(double Pm2_5);

/// <summary>Row of <c>SELECT COUNT(*) AS value</c>.</summary>
public sealed record CountRow(int Value);

/// <summary>Sensor columns the cron trigger needs to queue a job.</summary>
public sealed record DueSensor(string SensorId, int IntervalMinutes);

/// <summary>Where a sensor is, which is all the weather API call needs.</summary>
public sealed record SensorLocation(double Latitude, double Longitude);
