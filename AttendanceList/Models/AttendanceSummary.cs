namespace AttendanceList.Models;

public sealed record AttendanceSummary(int Present, int Absent, int Excused, int Unknown)
{
    public int Total => Present + Absent + Excused + Unknown;
}
