using AttendanceList.Models;

namespace AttendanceList.Services;

public static class SubjectLabels
{
    public static string ForClass(ClassGroup? classGroup) =>
        ForSnapshot(classGroup?.SubjectLabel);

    public static string ForSnapshot(string? subjectLabel) =>
        string.IsNullOrWhiteSpace(subjectLabel)
            ? LocalizationService.T("DefaultSubjectLabel")
            : subjectLabel.Trim();
}
