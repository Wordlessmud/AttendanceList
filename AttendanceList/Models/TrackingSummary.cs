namespace AttendanceList.Models;

public sealed record TrackingSummary(int Positive, int Negative, int Excused, int Unknown)
{
    public int Total => Positive + Negative + Excused + Unknown;
}
