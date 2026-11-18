namespace UtilityBills;

public class BillingRepository
{
    // TODO TD-06: статичний лічильник спільний для всіх екземплярів і не переживає перезапуск.
    private static int nextBillNo = 5001;

    private readonly List<Account> accounts = new List<Account>();
    private readonly List<Bill> bills = new List<Bill>();

    // R13: колекції тільки для читання, змінювати їх можна лише методами репозиторію
    public IReadOnlyList<Account> Accounts => accounts;
    public IReadOnlyList<Bill> Bills => bills;

    public void AddBill(Bill bill)
    {
        bill.Number = nextBillNo++;
        bills.Add(bill);
    }

    public BillingRepository()
    {
        // TODO TD-04: демо-дані зашиті в конструктор; винести в сховище або сідер.
        AddAccount("UB-1001", "Olena Kovalenko", "Sadova 4/12", 2, BenefitCategory.Standard, "1Z", false, 0);
        AddAccount("UB-1002", "Mykola Shevchenko", "Sadova 4/31", 1, BenefitCategory.Veteran, "1Z", false, 0);
        AddAccount("UB-1003", "Tetiana Bondarenko", "Lisova 9", 6, BenefitCategory.LargeFamily, "1Z", false, 600);
        AddAccount("UB-1004", "Ivan Petrenko", "Lisova 15", 3, BenefitCategory.Standard, "1Z", true, 0);
        AddAccount("UB-1005", "Hanna Moroz", "Sadova 6/7", 2, BenefitCategory.Chornobyl, "2Z", false, 250);
        // рахунки за минулий місяць зі старої бази
        SeedBill("UB-1002", "EL", 120, 262.8, BillStatus.Issued, -4);
        SeedBill("UB-1003", "WATER", 16, 600, BillStatus.Issued, 0);
        SeedBill("UB-1001", "GAS", 52, 451.42, BillStatus.Paid, -10);
    }

    private void AddAccount(string no, string owner, string addr, int residents,
        BenefitCategory category, string meter, bool eheat, double cap)
    {
        Account a = new Account();
        a.Number = no;
        a.Owner = owner;
        a.Address = addr;
        a.Residents = residents;
        a.Category = category;
        a.MeterType = meter;
        a.ElectricHeating = eheat;
        a.SubsidyCap = cap;
        accounts.Add(a);
    }

    private void SeedBill(string acc, string svc, double units, double amount, BillStatus st,
        int dueIn)
    {
        Bill b = new Bill();
        b.AccountNo = acc;
        b.Service = svc;
        b.Units = units;
        b.Amount = amount;
        b.Status = st;
        b.DueDate = DateTime.Today.AddDays(dueIn);
        b.IssuedOn = b.DueDate.AddDays(-20);
        if (st == BillStatus.Paid)
            b.PaidSum = amount;
        AddBill(b);
    }

    public Account FindAccount(string no)
    {
        foreach (Account a in accounts)
        {
            if (a.Number == no)
                return a;
        }
        return null;
    }

    public Bill FindBill(int no)
    {
        foreach (Bill b in bills)
        {
            if (b.Number == no)
                return b;
        }
        return null;
    }
}

public class AuditLog
{
    public static string FilePath =
        Path.Combine(Path.GetTempPath(), "utility_bills_audit.log");

    public static void Write(string msg)
    {
        try
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            File.AppendAllText(FilePath, stamp + " " + msg + "\n");
        }
        catch
        {
            // TODO TD-05: журнал аудиту мовчки ковтає помилки запису.
        }
    }
}
