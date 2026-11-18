namespace UtilityBills;

public class BillCalculator
{
    private const int StaleReadingDays = 45;
    private const double PenaltyDailyRate = 0.001;
    private const double PenaltyCapShare = 0.1;

    private BillingRepository repository;
    // R13: результати останнього Charge доступні лише для читання
    public double LastSubsidy { get; private set; }   // субсидія з останнього Charge
    public double LastUnits { get; private set; }     // одиниць у розрахунку
    public string LastNote { get; private set; } = "";

    public BillCalculator(BillingRepository repository)
    {
        this.repository = repository;
    }

    // Нарахування за одну послугу за місяць, грн.
    // Повертає -1, якщо особового рахунку немає.
    // TODO TD-03: -1/-2 як коди помилок; замінити на результат або виняток.
    // R11: усі вхідні дані зібрано в ChargeRequest, включно з швом для годинника.
    public double Charge(ChargeRequest request)
    {
        DateTime now = request.ResolveToday();
        LastSubsidy = 0;
        LastUnits = 0;
        LastNote = "";
        Account account = repository.FindAccount(request.AccountNumber);
        if (account == null)
        {
            Console.WriteLine("  [calc] account " + request.AccountNumber + " not found");
            return -1;
        }
        if (request.PreviousReading < 0 || request.CurrentReading < 0)
            return -2;
        string service = request.ServiceCode.Trim().ToUpper();
        double units = request.CurrentReading - request.PreviousReading;
        bool isStaleReading = (now.Date - request.ReadAt.Date).Days > StaleReadingDays;
        bool isHeatingSeason = ElectricityTariff.IsHeatingSeason(now);
        bool isNightMeter = account.MeterType == "2Z" && request.NightReading > 0;
        // перевіряємо, чи показник не застарий
        double nightReading = request.NightReading;
        if (isStaleReading)
        {
            units = NormUnits(service, account.Residents);
            nightReading = 0;
            LastNote = "by norm";
        }
        LastUnits = units;

        double sum = CalculateBase(service, units, nightReading, account,
            isHeatingSeason, isNightMeter);
        // пільги: ЧАЕС -50%, УБД і багатодітні -25%
        sum = ApplyBenefit(account, sum);
        sum = ApplySubsidy(account, sum);
        sum = Math.Round(sum, 2);
        AuditLog.Write("charge " + request.AccountNumber + " " + service + " units=" + units
            + " sum=" + sum + " subsidy=" + LastSubsidy);
        return sum;
    }

    // Тариф за послугою: електрика має сходинки й нічний тариф, газ і вода - прості ставки.
    // TODO TD-02: коди послуг - рядки; невідомий код мовчки рахується як вода.
    private static double CalculateBase(string service, double units, double nightReading,
        Account account, bool isHeatingSeason, bool isNightMeter)
    {
        if (service == "EL")
            return CalculateElectricity(units, nightReading, account,
                isHeatingSeason, isNightMeter);
        if (service == "GAS")
            return GasTariff.Calculate(units);
        return WaterTariff.Calculate(units);
    }

    // Субсидія обрізає суму до SubsidyCap, різниця зберігається в LastSubsidy.
    private double ApplySubsidy(Account account, double sum)
    {
        LastSubsidy = 0;
        if (account.SubsidyCap > 0 && sum > account.SubsidyCap)
        {
            LastSubsidy = Math.Round(sum - account.SubsidyCap, 2);
            return account.SubsidyCap;
        }
        return sum;
    }

    // Нарахування за електрику: сходинки, електроопалення, нічний тариф.
    private static double CalculateElectricity(double units, double nightReading,
        Account account, bool isHeatingSeason, bool isNightMeter)
    {
        double sum = ElectricityTariff.Calculate(units);
        // електроопалення, жовтень-квітень:
        // до 3000 кВт*год - за першим ступенем
        if (account.ElectricHeating && isHeatingSeason)
            sum = ElectricityTariff.CalculateHeating(units);
        // 2Z - двозонний, ніч за пів ціни
        if (isNightMeter)
            sum = ElectricityTariff.ApplyNightMeter(sum, nightReading, units);
        return sum;
    }

    private static double ApplyBenefit(Account account, double sum)
    {
        return account.Category.Apply(sum);
    }

    // норма на місяць, якщо показник не передали
    public double NormUnits(string service, int residents)
    {
        return ConsumptionNorms.For(service, residents);
    }

    // пеня 0.1% за день, не більше 10% суми
    public double CalcPenalty(Bill bill)
    {
        if (bill.Status != BillStatus.Overdue || bill.Amount <= 0)
            return 0;
        int daysOverdue = (DateTime.Today - bill.DueDate.Date).Days;
        double penalty = bill.Amount * PenaltyDailyRate * daysOverdue;
        if (penalty > bill.Amount * PenaltyCapShare)
            penalty = bill.Amount * PenaltyCapShare;
        return Math.Round(penalty, 2);
    }
}
