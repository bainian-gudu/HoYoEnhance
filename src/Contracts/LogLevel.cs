namespace HoYoEnhance.Contracts;

/// <summary>日志级别（数值越大越严重）。</summary>
[TsName("LogLevel")]
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
}
