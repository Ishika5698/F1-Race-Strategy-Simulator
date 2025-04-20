using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace F1SimulatorBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StrategyController : ControllerBase
    {
        // Historical data from 2021-2024 including lap counts, pit stats, and safety car trends
        private readonly Dictionary<string, (int Laps, int AvgPitStops, double Difficulty, double SafetyCarLikelihood, double AvgPitTime)> _trackHistory = new()
        {
            { "Bahrain", (57, 1, 1.0, 0.2, 22.0) }, { "Saudi Arabia", (50, 1, 1.1, 0.3, 21.5) }, { "Australia", (58, 1, 1.2, 0.4, 23.0) },
            { "Japan", (53, 2, 1.3, 0.3, 22.5) }, { "China", (56, 1, 1.1, 0.2, 22.0) }, { "Miami", (57, 1, 1.0, 0.3, 21.8) },
            { "Emilia-Romagna", (63, 1, 1.2, 0.4, 22.2) }, { "Monaco", (78, 2, 1.5, 0.6, 24.0) }, { "Spain", (66, 1, 1.1, 0.3, 22.0) },
            { "Canada", (70, 2, 1.3, 0.5, 23.5) }, { "Austria", (71, 1, 1.2, 0.4, 22.3) }, { "Great Britain", (52, 2, 1.3, 0.4, 22.8) },
            { "Hungary", (70, 2, 1.4, 0.3, 23.0) }, { "Belgium", (44, 1, 1.2, 0.5, 22.5) }, { "Netherlands", (72, 1, 1.1, 0.3, 21.9) },
            { "Italy", (53, 1, 1.0, 0.2, 22.0) }, { "Azerbaijan", (51, 2, 1.4, 0.5, 23.2) }, { "Singapore", (62, 2, 1.5, 0.6, 24.5) },
            { "United States", (56, 1, 1.1, 0.3, 22.1) }, { "Mexico", (71, 1, 1.2, 0.4, 22.4) }, { "Brazil", (71, 2, 1.3, 0.5, 23.3) },
            { "Las Vegas", (50, 1, 1.1, 0.3, 21.7) }, { "Qatar", (57, 1, 1.2, 0.4, 22.3) }, { "Abu Dhabi", (58, 1, 1.0, 0.2, 22.0) }
        };
        private readonly Dictionary<string, double> _weatherImpact = new()
        {
            { "Sunny", 1.0 }, { "Rain", 1.5 }, { "Overcast", 1.2 }
        };
        private readonly Dictionary<string, string[]> _optimalTiresByTrack = new()
        {
            { "Bahrain", new[] { "Medium", "Hard" } }, { "Monaco", new[] { "Soft", "Medium" } },
            { "Singapore", new[] { "Soft", "Intermediates" } }, { "Great Britain", new[] { "Medium", "Intermediates" } },
            { "Canada", new[] { "Soft", "Intermediates" } }, { "China", new[] { "Medium", "Intermediates" } },
            { "Emilia-Romagna", new[] { "Medium", "Hard" } }, { "United States", new[] { "Medium", "Hard" } },
            { "Belgium", new[] { "Medium", "Hard", "Intermediates" } }, { "Brazil", new[] { "Medium", "Intermediates" } },
            { "Abu Dhabi", new[] { "Medium", "Hard" } }
        };
        private readonly Dictionary<int, double> _safetyCarLikelihoodByLap = new()
        {
            { 5, 0.1 }, { 10, 0.15 }, { 20, 0.2 }, { 30, 0.25 }, { 40, 0.2 }, { 50, 0.15 }, { 60, 0.1 }
        };

        [HttpGet("calculate")]
        public IActionResult CalculateStrategy(string track = "Bahrain", string weather = "Sunny", bool redFlag = false, int redFlagLap = 0)
        {
            // Get historical data for the track
            var trackData = _trackHistory.ContainsKey(track) ? _trackHistory[track] : (57, 1, 1.0, 0.2, 22.0);
            int laps = trackData.Item1;
            int historicalPitStops = trackData.Item2;
            double difficultyFactor = trackData.Item3;
            double safetyCarLikelihood = trackData.Item4;
            double avgPitTime = trackData.Item5;

            // Tire wear model: degradation factor based on track and weather
            double tireWearFactor = 1.0 + (difficultyFactor * 0.1) + (_weatherImpact[weather] - 1.0) * 0.3;
            int maxStintLaps = (int)(35 / tireWearFactor); // Increased base to 35 for longer stints

            // Intelligent pit stop prediction with safety car and red flag
            int basePitStops = (int)Math.Ceiling(laps / (double)maxStintLaps);
            int adjustedPitStops = (int)(historicalPitStops * (1 + 0.1 * difficultyFactor) * (_weatherImpact[weather] / 1.0)); // Reduced multiplier
            adjustedPitStops = Math.Max(historicalPitStops, Math.Max(basePitStops, adjustedPitStops)); // Enforce historical as minimum
            if (laps <= 50 && !redFlag && weather == "Sunny") adjustedPitStops = Math.Min(1, adjustedPitStops); // Favor 1 stop for short Sunny races
            double currentSafetyCarLikelihood = _safetyCarLikelihoodByLap.OrderBy(kvp => Math.Abs(kvp.Key - (redFlagLap > 0 ? redFlagLap : laps / 2))).First().Value;
            int extraStops = 0;
            if (redFlag && redFlagLap > 0 && redFlagLap < laps) extraStops = 1;
            else if (new Random().NextDouble() < Math.Max(0.3, currentSafetyCarLikelihood)) extraStops = 1;
            adjustedPitStops += extraStops;
            adjustedPitStops = Math.Min(redFlag ? historicalPitStops + 2 : historicalPitStops + 1, adjustedPitStops);

            // Adjust pit laps with safety car and red flag
            int lapsPerStint = laps / Math.Max(1, adjustedPitStops - (redFlag && redFlagLap > 0 ? 1 : 0));
            var pitLaps = new List<int>();
            if (redFlag && redFlagLap > 0 && redFlagLap <= laps) pitLaps.Add(redFlagLap); // Pit at red flag
            for (int i = 1; i <= adjustedPitStops - (redFlag && redFlagLap > 0 ? 1 : 0); i++)
            {
                int lap = redFlagLap > 0 ? redFlagLap + (i * lapsPerStint) - (i == adjustedPitStops - (redFlag ? 1 : 0) ? 0 : (int)(lapsPerStint * 0.3)) : i * lapsPerStint - (i == adjustedPitStops ? 0 : (int)(lapsPerStint * 0.3));
                lap = Math.Max(redFlagLap > 0 ? redFlagLap + 1 : 1, Math.Min(lap, laps));
                if (!pitLaps.Contains(lap)) pitLaps.Add(lap);
            }
            if (pitLaps.Count < adjustedPitStops) pitLaps.Add(laps);

            // Intelligent tire recommendation with wear consideration
            string[] optimalTires = _optimalTiresByTrack.ContainsKey(track) ? _optimalTiresByTrack[track] : new[] { "Medium", "Hard" };
            string recommendedTire = optimalTires[0];
            int remainingLaps = laps - (redFlag && redFlagLap > 0 ? redFlagLap : 0);
            if (weather == "Rain") recommendedTire = "Intermediates";
            else if (weather == "Overcast" && maxStintLaps < 25) recommendedTire = optimalTires.Contains("Intermediates") ? "Intermediates" : optimalTires[1];
            else if (remainingLaps > maxStintLaps * 0.8 && optimalTires.Length > 1) recommendedTire = optimalTires[1];

            // Concise reasoning
            string reasoning = $"Reasoning: Based on {track}'s 2021-2024 data ({historicalPitStops} avg pit stops, difficulty x{difficultyFactor}, " +
                              $"safety car likelihood {safetyCarLikelihood*100:F0}%), adjusted for {laps} laps. Tire wear factor {tireWearFactor:F1} " +
                              $"limits stints to {maxStintLaps} laps. {weather} weather (x{_weatherImpact[weather]}) and " +
                              $"{(redFlag ? $"red flag at lap {redFlagLap}" : $"safety car likelihood {currentSafetyCarLikelihood*100:F0}% at lap {(redFlagLap > 0 ? redFlagLap : laps / 2)}")} " +
                              $"influenced {adjustedPitStops} stops. Tire: {recommendedTire} from {string.Join(", ", optimalTires)} due to wear.";

            string strategy = $"Strategy for {track}: {adjustedPitStops} pit stop(s) at laps {string.Join(", ", pitLaps)}. " +
                             $"Recommended tire: {recommendedTire}. {reasoning}";

            return Ok(new { strategy, totalLaps = laps, pitLaps, recommendedTire, reasoning });
        }
    }
}