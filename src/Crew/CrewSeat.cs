namespace MedusaMultiCrew.Crew
{
    internal static class CrewSeat
    {
        public const byte Pilot = 0;
        public const byte Wso1 = 1;
        public const byte Wso2 = 2;

        public static bool IsWso(byte seat) => seat == Wso1 || seat == Wso2;

        public static string Name(byte seat)
        {
            switch (seat)
            {
                case Pilot: return "Pilot";
                case Wso1: return "WSO 1";
                case Wso2: return "WSO 2";
                default: return $"seat {seat}";
            }
        }
    }
}
