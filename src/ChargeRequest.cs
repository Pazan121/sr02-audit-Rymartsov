namespace UtilityBills;

/// <summary>
/// Параметри одного нарахування за послугу (R11).
/// Замінює шість позиційних аргументів Charge, щоб порядок і сенс
/// значень не губилися на місці виклику.
/// </summary>
public sealed class ChargeRequest
{
    public ChargeRequest(string accountNumber, string serviceCode, double previousReading,
        double currentReading, double nightReading, DateTime readAt)
    {
        AccountNumber = accountNumber;
        ServiceCode = serviceCode;
        PreviousReading = previousReading;
        CurrentReading = currentReading;
        NightReading = nightReading;
        ReadAt = readAt;
    }

    public string AccountNumber { get; }
    public string ServiceCode { get; }
    public double PreviousReading { get; }
    public double CurrentReading { get; }
    public double NightReading { get; }
    public DateTime ReadAt { get; }

    /// <summary>Шов для тестів: підміняє системний годинник; null - DateTime.Today.</summary>
    public DateTime? Today { get; init; }

    public DateTime ResolveToday()
    {
        return Today ?? DateTime.Today;
    }
}
