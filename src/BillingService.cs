namespace UtilityBills;

public class BillingService
{
    private BillingRepository repo;
    private BillCalculator calc;
    public string LastError;

    public BillingService(BillingRepository repo)
    {
        this.repo = repo;
        calc = new BillCalculator(repo);
    }

    // новий рахунок (Draft) за показниками лічильника
    public Bill CreateDraftBill(string accNo, string svc, double prev, double cur,
        double night, DateTime readAt)
    {
        return CreateBillCore(accNo, svc, prev, cur, night, readAt);
    }

    // той самий рахунок, одразу виданий платнику
    public Bill CreateIssuedBill(string accNo, string svc, double prev, double cur,
        double night, DateTime readAt)
    {
        Bill b = CreateBillCore(accNo, svc, prev, cur, night, readAt);
        if (b != null)
            ChangeStatus(b.Number, BillStatus.Issued);
        return b;
    }

    private Bill CreateBillCore(string accNo, string svc, double prev, double cur, double night,
        DateTime readAt)
    {
        LastError = null;
        if (svc != "EL" && svc != "GAS" && svc != "WATER")
        {
            LastError = "unknown service " + svc;
            return null;
        }
        double amt = calc.Charge(new ChargeRequest
        {
            AccountNumber = accNo,
            ServiceCode = svc,
            PreviousReading = prev,
            CurrentReading = cur,
            NightReading = night,
            ReadAt = readAt
        });
        // -1 - немає рахунку
        if (amt == -1)
        {
            LastError = "account " + accNo + " not found";
            return null;
        }
        if (amt == -2)
        {
            LastError = "invalid meter reading";
            return null;
        }
        Bill b = new Bill();
        b.AccountNo = accNo;
        b.Service = svc;
        b.PrevReading = prev;
        b.CurReading = cur;
        b.Units = calc.LastUnits;
        b.Amount = amt;
        b.Subsidy = calc.LastSubsidy;
        b.Note = calc.LastNote;
        repo.AddBill(b);
        return b;
    }

    // Таблиця дозволених переходів станів рахунку.
    private static readonly HashSet<(BillStatus From, BillStatus To)> AllowedTransitions =
        new HashSet<(BillStatus From, BillStatus To)>
        {
            (BillStatus.Draft, BillStatus.Issued),
            (BillStatus.Draft, BillStatus.Cancelled),
            (BillStatus.Issued, BillStatus.Paid),
            (BillStatus.Issued, BillStatus.Overdue),
            (BillStatus.Issued, BillStatus.Cancelled),
            (BillStatus.Overdue, BillStatus.Paid),
        };

    // Draft -> Issued/Cancelled, Issued -> Paid/Overdue/Cancelled, Overdue -> Paid
    public bool ChangeStatus(int no, BillStatus to)
    {
        Bill b = repo.FindBill(no);
        if (b == null)
            return false;
        if (!IsTransitionAllowed(b.Status, to))
        {
            Console.WriteLine("  ! bill " + b.Number + ": " + b.Status + " -> " + to
                + " not allowed");
            return false;
        }
        if (to == BillStatus.Issued)
        {
            b.IssuedOn = DateTime.Today;
            b.DueDate = DateTime.Today.AddDays(20);
        }
        if (to == BillStatus.Paid)
            b.PaidOn = DateTime.Today;
        b.Status = to;
        AuditLog.Write("bill " + b.Number + " -> " + to);
        return true;
    }

    // оплата, можна частинами
    public bool Pay(int no, double amount)
    {
        Bill b = repo.FindBill(no);
        if (b == null || amount <= 0)
            return false;
        if (b.IsOpen())
        {
            b.PaidSum = Math.Round(b.PaidSum + amount, 2);
            // R15: замість повторного виразу Amount + Penalty питаємо сам рахунок
            if (b.ToPay() <= 0)
                return ChangeStatus(no, BillStatus.Paid);
            return true;
        }
        Console.WriteLine("  ! bill " + no + " is " + b.Status + ", payment rejected");
        return false;
    }

    // прострочені - термін оплати вже минув
    public int RefreshOverdue()
    {
        int marked = MarkOverdueBills();
        RecalculatePenalties();
        return marked;
    }

    // R16: два проходи замість одного змішаного циклу - спочатку стани, потім пеня
    private int MarkOverdueBills()
    {
        int cnt = 0;
        foreach (Bill b in repo.Bills)
        {
            if (b.Status == BillStatus.Issued && b.DueDate <= DateTime.Today)
            {
                b.Status = BillStatus.Overdue;
                cnt++;
            }
        }
        return cnt;
    }

    private void RecalculatePenalties()
    {
        foreach (Bill b in repo.Bills)
        {
            if (b.Status == BillStatus.Overdue)
                b.Penalty = calc.CalcPenalty(b);
        }
    }

    // перерахунок після повірки лічильника (EL)
    // TODO: газ і воду поки коригує бухгалтерія вручну
    public bool CorrectBill(int no, double u)
    {
        Bill b = repo.FindBill(no);
        if (b == null || b.Service != "EL")
            return false;
        if (b.Status == BillStatus.Paid || b.Status == BillStatus.Cancelled)
            return false;
        double sum = ElectricityTariff.Calculate(u);
        Account a = repo.FindAccount(b.AccountNo);
        if (a.Category == "CHN")
            sum = sum * 0.5;
        else if (a.Category == "VET" || a.Category == "LRG")
            sum = sum * 0.75;
        double subsidy = 0;
        if (a.SubsidyCap > 0 && sum > a.SubsidyCap)
        {
            subsidy = sum - a.SubsidyCap;
            sum = a.SubsidyCap;
        }
        b.Units = u;
        b.Subsidy = Math.Round(subsidy, 2);
        b.Amount = Math.Round(sum, 2);
        b.Note = "corrected";
        AuditLog.Write("correction " + no + " -> " + b.Amount);
        return true;
    }

    private static bool IsTransitionAllowed(BillStatus from, BillStatus to)
    {
        return AllowedTransitions.Contains((from, to));
    }
}
