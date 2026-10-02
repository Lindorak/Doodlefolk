using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace StickFight;

/// <summary>Your real weather (opt-in): with the weather set to "real" and a place chosen, the sky follows the actual
/// weather there (rain, storms, snow, clear) and figures feel the temperature. It uses Open-Meteo (free, no key): the
/// place name you type is looked up once, then only the coordinates are sent, every 20 minutes. Nothing else leaves
/// this PC.</summary>
sealed partial class App
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    double _realWxAt;
    volatile bool _realWxBusy;
    (int code, float temp, float wind, bool day)? _realWxPending;
    public string RealWeatherStatus = "";

    void RealWeatherFrame(double now)
    {
        if (_settings.WeatherMode != "real" || _settings.WeatherLat is not double lat || _settings.WeatherLon is not double lon)
        {
            _w.TempC = null;
            return;
        }
        if (_realWxPending is { } wx)
        {
            _realWxPending = null;
            ApplyRealWeather(wx.code, wx.temp, wx.wind, now);
        }
        if (now < _realWxAt || _realWxBusy) return;
        _realWxAt = now + 1200;
        _realWxBusy = true;
        string url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={lat:0.###}&longitude={lon:0.###}&current=temperature_2m,weather_code,wind_speed_10m,is_day");
        _ = Task.Run(async () =>
        {
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                var cur = doc.RootElement.GetProperty("current");
                _realWxPending = (cur.GetProperty("weather_code").GetInt32(), (float)cur.GetProperty("temperature_2m").GetDouble(),
                                  (float)cur.GetProperty("wind_speed_10m").GetDouble(), cur.TryGetProperty("is_day", out var d) && d.GetInt32() == 1);
            }
            catch (Exception e)
            {
                World.Log("real weather: " + e.Message);
                RealWeatherStatus = "Couldn't reach the weather service; will try again shortly.";
                _realWxAt = _clock.Elapsed.TotalSeconds + 300;
            }
            finally { _realWxBusy = false; }
        });
    }

    static (WeatherKind kind, string words) WeatherFromCode(int code) => code switch
    {
        0 => (WeatherKind.Clear, "clear skies"),
        1 or 2 => (WeatherKind.Clear, "partly cloudy"),
        3 => (WeatherKind.Clear, "overcast"),
        45 or 48 => (WeatherKind.Clear, "foggy"),
        >= 51 and <= 57 => (WeatherKind.Rain, "drizzle"),
        >= 61 and <= 67 => (WeatherKind.Rain, "rain"),
        >= 71 and <= 77 => (WeatherKind.Snow, "snow"),
        >= 80 and <= 82 => (WeatherKind.Rain, "showers"),
        85 or 86 => (WeatherKind.Snow, "snow showers"),
        >= 95 => (WeatherKind.Storm, "thunderstorms"),
        _ => (WeatherKind.Clear, "fair"),
    };

    void ApplyRealWeather(int code, float temp, float wind, double now)
    {
        var (kind, words) = WeatherFromCode(code);
        _w.TempC = temp;
        _w.Weather.Hold(kind, now, _w.Rng, _w, wind);
        RealWeatherStatus = $"{_settings.WeatherPlace}: {words}, {temp:0}°C, wind {wind:0} km/h (updated {DateTime.Now:HH:mm})";
        World.Log("real weather: " + RealWeatherStatus);
        _w.Sticker("realweather");
    }

    /// <summary>Look a place name up (Open-Meteo geocoding) and use it for the real weather.</summary>
    void SetWeatherPlace(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 80) return;
        RealWeatherStatus = $"Looking up {name}…";
        string url = $"https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name={Uri.EscapeDataString(name)}";
        _ = Task.Run(async () =>
        {
            string status;
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
                if (doc.RootElement.TryGetProperty("results", out var res) && res.GetArrayLength() > 0)
                {
                    var r = res[0];
                    string place = r.GetProperty("name").GetString() ?? name;
                    if (r.TryGetProperty("admin1", out var a1) && a1.GetString() is { Length: > 0 } admin && admin != place) place += ", " + admin;
                    if (r.TryGetProperty("country", out var c) && c.GetString() is { Length: > 0 } country) place += ", " + country;
                    double lat = r.GetProperty("latitude").GetDouble(), lon = r.GetProperty("longitude").GetDouble();
                    _overlay.BeginInvoke(() =>
                    {
                        _settings.WeatherPlace = place; _settings.WeatherLat = lat; _settings.WeatherLon = lon;
                        _settings.WeatherMode = "real";
                        _settings.Save();
                        _realWxAt = 0;
                        RealWeatherStatus = $"Found {place}. Fetching the weather…";
                                    });
                    return;
                }
                status = $"Couldn't find \"{name}\". Try a nearby town or city.";
            }
            catch (Exception e) { status = "Couldn't reach the weather service: " + e.Message; }
            _overlay.BeginInvoke(() => { RealWeatherStatus = status; });
        });
    }
}
