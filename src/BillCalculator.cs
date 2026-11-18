namespace UtilityBills;

public class BillCalculator
{
    private const double ChornobylDiscount = 0.5;
    private const double StandardBenefitDiscount = 0.75;
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
    // R11: усі вхідні дані зібрано в ChargeRequest, включно з швом для годинника.
    public double Charge(ChargeRequest request)
    {
        DateTime now = request.Today ?? DateTime.Today;
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

        double sum;
        if (service == "EL")
            sum = CalculateElectricity(units, nightReading, account,
                isHeatingSeason, isNightMeter);
        else if (service == "GAS")
            sum = GasTariff.Calculate(units);
        else
            sum = WaterTariff.Calculate(units);

        // пільги: ЧАЕС -50%, УБД і багатодітні -25%
        sum = ApplyBenefit(account, sum);
        double subsidy = 0;
        if (account.SubsidyCap > 0 && sum > account.SubsidyCap)
        {
            subsidy = sum - account.SubsidyCap;
            sum = account.SubsidyCap;
        }
        LastSubsidy = Math.Round(subsidy, 2);
        sum = Math.Round(sum, 2);
        AuditLog.Write("charge " + request.AccountNumber + " " + service + " units=" + units
            + " sum=" + sum + " subsidy=" + LastSubsidy);
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
        if (account.Category == "CHN")
            return sum * ChornobylDiscount;
        if (account.Category == "VET" || account.Category == "LRG")
            return sum * StandardBenefitDiscount;
        return sum;
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
