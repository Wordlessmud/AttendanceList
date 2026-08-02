namespace AttendanceList.Models;

public sealed class ReportRow
{
    public string Name { get; init; } = string.Empty;
    public int Present { get; init; }
    public int Absent { get; init; }
    public int Excused { get; init; }
    public int Unknown { get; init; }
    public double? Rate => Present + Absent == 0 ? null : (double)Present / (Present + Absent);
    public string RateText => Rate is null ? "—" : Rate.Value.ToString("P0");
}
