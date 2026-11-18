namespace UtilityBills;

/// <summary>
/// Параметри одного нарахування за послугу (R11).
/// Замінює шість позиційних аргументів Charge, щоб порядок і сенс
/// значень не губилися на місці виклику.
/// </summary>
public sealed class ChargeRequest
{
    public string AccountNumber { get; init; }
    public string ServiceCode { get; init; }
    public double PreviousReading { get; init; }
    public double CurrentReading { get; init; }
    public double NightReading { get; init; }
    public DateTime ReadAt { get; init; }

    /// <summary>Шов для тестів: підміняє системний годинник; null - DateTime.Today.</summary>
    public DateTime? Today { get; init; }
}
