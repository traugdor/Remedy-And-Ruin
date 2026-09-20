using Vintagestory.API.Common;

namespace Remedy_And_Ruin.GameEngineTweaks
{
    public static class CalendarTimeHelper
    {
        public static double RealSecondsPerGameHour(IGameCalendar calendar)
        {
            return 3600.0 / (calendar.SpeedOfTime * calendar.CalendarSpeedMul);
        }

        public static double GameHoursToRealSeconds(IGameCalendar calendar, double gameHours)
        {
            return gameHours * RealSecondsPerGameHour(calendar);
        }

        public static double RealSecondsToGameHours(IGameCalendar calendar, double realSeconds)
        {
            return realSeconds / RealSecondsPerGameHour(calendar);
        }
    }
}
