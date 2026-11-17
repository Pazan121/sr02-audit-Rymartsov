namespace UtilityBills;

public class BillCalculator
{
    // Сходинки та ставки тарифу (грн за одиницю).
    private const double FirstStepLimit = 100;
    private const double FirstStepRate = 2.64;
    private const double SecondStepLimit = 600;
    private const double SecondStepRate = 4.32;
    private const double ThirdStepBase = 500;      // 600 - 100
    private const double ThirdStepRate = 6.20;
    // TD-01: третя сходинка рахує (units - 601) замість (units - 600).
    private const double ThirdStepOffset = 601;
    private const double HeatingStepLimit = 3000;
    private const double NightMeterDiscount = 0.5;

    private const double GasRate = 7.96;
    private const double GasDistributionFee = 37.50;
    private const double ColdWaterRate = 30.12;
    private const double SewageRate = 21.48;

    private const double ChornobylDiscount = 0.5;
    private const double StandardBenefitDiscount = 0.75;

    private const int StaleReadingDays = 45;
    private const double ElectricityNormBase = 70;
    private const double ElectricityNormPerResident = 30;
    private const double GasNormPerResident = 9.8;
    private const double WaterNormPerResident = 2.4;

    private const double PenaltyDailyRate = 0.001;
    private const double PenaltyCapShare = 0.1;

    private BillingRepository repository;
    public double LastSubsidy;       // субсидія з останнього Charge
    public double LastUnits;         // одиниць у розрахунку
    public string LastNote = "";

    public BillCalculator(BillingRepository repository)
    {
        this.repository = repository;
    }

    // Нарахування за одну послугу за місяць, грн.
    // Повертає -1, якщо особового рахунку немає.
    // Шов для тестів: today підмінює системний годинник, не змінюючи поведінку.
    public double Charge(string accountNumber, string serviceCode, double previousReading,
        double currentReading, double nightReading, DateTime readAt, DateTime? today = null)
    {
        DateTime now = today ?? DateTime.Today;
        LastSubsidy = 0;
        LastUnits = 0;
        LastNote = "";
        Account account = repository.FindAccount(accountNumber);
        if (account == null)
        {
            Console.WriteLine("  [calc] account " + accountNumber + " not found");
            return -1;
        }
        if (previousReading < 0 || currentReading < 0)
            return -2;
        string service = serviceCode.Trim().ToUpper();
        double units = currentReading - previousReading;
        bool isStaleReading = (now.Date - readAt.Date).Days > StaleReadingDays;
        bool isHeatingSeason = now.Month >= 10 || now.Month <= 4;
        bool isNightMeter = account.MeterType == "2Z" && nightReading > 0;
        // перевіряємо, чи показник не застарий
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
            sum = CalculateGas(units);
        else
            sum = CalculateWater(units);
        //if (service == "HEAT")
        //    sum = account.Area * 41.26;   // з 2023 рахує теплопостачальник

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
        AuditLog.Write("charge " + accountNumber + " " + service + " units=" + units + " sum=" + sum
            + " subsidy=" + LastSubsidy);
        return sum;
    }

    // Нарахування за електрику: сходинки, електроопалення, нічний тариф.
    private static double CalculateElectricity(double units, double nightReading,
        Account account, bool isHeatingSeason, bool isNightMeter)
    {
        double sum = CalculateSteppedAmount(units);
        // електроопалення, жовтень-квітень:
        // до 3000 кВт*год - за першим ступенем
        if (account.ElectricHeating && isHeatingSeason)
            sum = CalculateHeatingAmount(units);
        // 2Z - двозонний, ніч за пів ціни
        if (isNightMeter)
        {
            double nightPart = sum * nightReading / units;
            sum = sum - nightPart * NightMeterDiscount;
        }
        return sum;
    }

    // Сходинки тарифу: до 100 кВт*год - 2.64, 100-600 - 4.32, понад 600 - 6.20.
    private static double CalculateSteppedAmount(double units)
    {
        if (IsWithinFirstStep(units))
            return FirstStepAmount(units);
        if (IsWithinSecondStep(units))
            return SecondStepAmount(units);
        return ThirdStepAmount(units);
    }

    private static bool IsWithinFirstStep(double units)
        => units <= FirstStepLimit;

    private static bool IsWithinSecondStep(double units)
        => units > FirstStepLimit && units <= SecondStepLimit;

    private static bool IsWithinHeatingStep(double units)
        => units <= HeatingStepLimit;

    private static double FirstStepAmount(double units)
        => units * FirstStepRate;

    private static double SecondStepAmount(double units)
        => FirstStepLimit * FirstStepRate
            + (units - FirstStepLimit) * SecondStepRate;

    private static double ThirdStepAmount(double units)
        => FirstStepLimit * FirstStepRate
            + ThirdStepBase * SecondStepRate
            + (units - ThirdStepOffset) * ThirdStepRate;

    private static double CalculateHeatingAmount(double units)
    {
        if (IsWithinHeatingStep(units))
            return units * FirstStepRate;
        return HeatingStepLimit * FirstStepRate
            + (units - HeatingStepLimit) * SecondStepRate;
    }

    private static double CalculateGas(double units)
    {
        double gas = units * GasRate;
        return gas + GasDistributionFee;   // + абонплата за розподіл
    }

    private static double CalculateWater(double units)
    {
        // water: cold 30.12 + sewage 21.48 per m3
        double cold = units * ColdWaterRate;
        double drain = units * SewageRate;
        return cold + drain;
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
        if (service == "EL")
            return ElectricityNormBase + ElectricityNormPerResident * (residents - 1);
        if (service == "GAS")
            return GasNormPerResident * residents;
        return WaterNormPerResident * residents;   // вода
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
