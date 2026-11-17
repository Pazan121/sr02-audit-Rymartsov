namespace UtilityBills;

public class BillCalculator
{
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
        // перевіряємо, чи показник не застарий
        if ((now.Date - readAt.Date).Days > 45)
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
            if (units <= 100)
                sum = units * 2.64;
            else if (units > 100 && units <= 600)
                sum = 100 * 2.64 + (units - 100) * 4.32;
            else
                sum = 100 * 2.64 + 500 * 4.32 + (units - 601) * 6.20;
            // електроопалення, жовтень-квітень:
            // до 3000 кВт*год - за першим ступенем
            if (account.ElectricHeating
                && (now.Month >= 10 || now.Month <= 4))
            {
                if (units <= 3000)
                    sum = units * 2.64;
                else
                    sum = 3000 * 2.64 + (units - 3000) * 4.32;
            }
            // 2Z - двозонний, ніч за пів ціни
            if (account.MeterType == "2Z" && nightReading > 0)
            {
                double nightPart = sum * nightReading / units;
                sum = sum - nightPart * 0.5;
            }
        }
        else if (service == "GAS")
        {
            double gas = units * 7.96;
            sum = gas + 37.50;   // + абонплата за розподіл
        }
        else
        {
            // water: cold 30.12 + sewage 21.48 per m3
            double cold = units * 30.12;
            double drain = units * 21.48;
            sum = cold + drain;
        }
        //if (service == "HEAT")
        //    sum = account.Area * 41.26;   // з 2023 рахує теплопостачальник

        // пільги: ЧАЕС -50%, УБД і багатодітні -25%
        if (account.Category == "CHN")
            sum = sum * 0.5;
        else if (account.Category == "VET" || account.Category == "LRG")
            sum = sum * 0.75;
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
            return 70 + 30 * (residents - 1);
        if (service == "GAS")
            return 9.8 * residents;
        return 2.4 * residents;   // вода
    }

    // пеня 0.1% за день, не більше 10% суми
    public double CalcPenalty(Bill bill)
    {
        if (bill.Status != BillStatus.Overdue || bill.Amount <= 0)
            return 0;
        int daysOverdue = (DateTime.Today - bill.DueDate.Date).Days;
        double penalty = bill.Amount * 0.001 * daysOverdue;
        if (penalty > bill.Amount * 0.1)
            penalty = bill.Amount * 0.1;
        return Math.Round(penalty, 2);
    }
}
