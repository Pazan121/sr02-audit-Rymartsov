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

        double sum = 0;
        if (service == "EL")
        {
            // steps: up to 100 kWh incl. - 2.64, 100-600 - 4.32, over 600 - 6.20
            if (units <= FirstStepLimit)
                sum = units * FirstStepRate;
            else if (units > FirstStepLimit && units <= SecondStepLimit)
                sum = FirstStepLimit * FirstStepRate
                    + (units - FirstStepLimit) * SecondStepRate;
            else
                sum = FirstStepLimit * FirstStepRate
                    + ThirdStepBase * SecondStepRate
                    + (units - ThirdStepOffset) * ThirdStepRate;
            // електроопалення, жовтень-квітень:
            // до 3000 кВт*год - за першим ступенем
            if (account.ElectricHeating && isHeatingSeason)
            {
                if (units <= HeatingStepLimit)
                    sum = units * FirstStepRate;
                else
                    sum = HeatingStepLimit * FirstStepRate
                        + (units - HeatingStepLimit) * SecondStepRate;
            }
            // 2Z - двозонний, ніч за пів ціни
            if (isNightMeter)
            {
                double nightPart = sum * nightReading / units;
                sum = sum - nightPart * NightMeterDiscount;
            }
        }
        else if (service == "GAS")
        {
            double gas = units * GasRate;
            sum = gas + GasDistributionFee;   // + абонплата за розподіл
        }
        else
        {
            // water: cold 30.12 + sewage 21.48 per m3
            double cold = units * ColdWaterRate;
            double drain = units * SewageRate;
            sum = cold + drain;
        }
        //if (service == "HEAT")
        //    sum = account.Area * 41.26;   // з 2023 рахує теплопостачальник

        // пільги: ЧАЕС -50%, УБД і багатодітні -25%
        if (account.Category == "CHN")
            sum = sum * ChornobylDiscount;
        else if (account.Category == "VET" || account.Category == "LRG")
            sum = sum * StandardBenefitDiscount;
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
