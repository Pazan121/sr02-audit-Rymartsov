namespace UtilityBills;

/// <summary>
/// Пільгова категорія мешканця (R12). Замінює рядковий код "STD"/"VET"/"CHN"/"LRG":
/// тепер і знижка, і назва для звіту живуть в одному об'єкті.
/// </summary>
public sealed class BenefitCategory
{
    public static readonly BenefitCategory Standard =
        new BenefitCategory("STD", "standard", 1.0);
    public static readonly BenefitCategory Veteran =
        new BenefitCategory("VET", "combat veteran -25%", 0.75);
    public static readonly BenefitCategory Chornobyl =
        new BenefitCategory("CHN", "Chornobyl -50%", 0.5);
    public static readonly BenefitCategory LargeFamily =
        new BenefitCategory("LRG", "large family -25%", 0.75);

    private static readonly BenefitCategory[] Known =
        { Standard, Veteran, Chornobyl, LargeFamily };

    public string Code { get; }
    public string DisplayName { get; }
    public double DiscountShare { get; }

    private BenefitCategory(string code, string displayName, double discountShare)
    {
        Code = code;
        DisplayName = displayName;
        DiscountShare = discountShare;
    }

    /// <summary>Код з довідника; невідомий код зберігається без знижки.</summary>
    public static BenefitCategory FromCode(string code)
    {
        foreach (BenefitCategory c in Known)
        {
            if (c.Code == code)
                return c;
        }
        return new BenefitCategory(code, code, 1.0);
    }

    /// <summary>Сума після пільги.</summary>
    public double Apply(double amount)
    {
        return amount * DiscountShare;
    }
}
