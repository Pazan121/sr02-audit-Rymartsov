using System;
using System.IO;
using UtilityBills;
using Xunit;

namespace Legacy.Tests;

/// <summary>
/// Характеризаційні тести сценаріїв рахунку: створення, переходи станів,
/// оплата, прострочення, перерахунок і звіти.
/// </summary>
public class BillingServiceTests
{
    private static readonly DateTime Yesterday = DateTime.Today.AddDays(-1);

    private static BillingService NewService(out BillingRepository repo)
    {
        repo = new BillingRepository();
        return new BillingService(repo);
    }

    private static Bill NewDraft(BillingService billing, bool issueNow = false)
    {
        return billing.CreateBill("UB-1001", "EL", 12000, 12240, 0, Yesterday, issueNow);
    }

    [Fact]
    public void CreateBill_Електрика240кВт_СтворюєЧернетку()
    {
        var billing = NewService(out _);

        var bill = NewDraft(billing);

        Assert.NotNull(bill);
        Assert.Equal("UB-1001", bill.AccountNo);
        Assert.Equal(240, bill.Units, 6);
        Assert.Equal(868.80, bill.Amount, 6);
        Assert.Equal(BillStatus.Draft, bill.Status);
    }

    [Fact]
    public void CreateBill_НевідомаПослуга_ПовертаєNull()
    {
        var billing = NewService(out _);

        var bill = billing.CreateBill("UB-1001", "HEAT", 0, 40, 0, Yesterday, false);

        Assert.Null(bill);
        Assert.Contains("unknown service", billing.LastError);
    }

    [Fact]
    public void CreateBill_НевідомийРахунок_ПовертаєNull()
    {
        var billing = NewService(out _);

        var bill = billing.CreateBill("UB-7777", "GAS", 0, 10, 0, Yesterday, false);

        Assert.Null(bill);
        Assert.Contains("not found", billing.LastError);
    }

    [Fact]
    public void CreateBill_ВідємнийПоказник_ПовертаєNull()
    {
        var billing = NewService(out _);

        var bill = billing.CreateBill("UB-1002", "EL", 8650, -10, 0, Yesterday, false);

        Assert.Null(bill);
        Assert.Contains("invalid meter reading", billing.LastError);
    }

    [Fact]
    public void ChangeStatus_ЧернеткаВВиданий_Дозволено()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing);

        bool ok = billing.ChangeStatus(bill.Number, BillStatus.Issued);

        Assert.True(ok);
        Assert.Equal(BillStatus.Issued, bill.Status);
        Assert.Equal(DateTime.Today, bill.IssuedOn.Date);
        Assert.Equal(DateTime.Today.AddDays(20), bill.DueDate.Date);
    }

    [Fact]
    public void ChangeStatus_ЧернеткаВОплачений_Заборонено()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing);

        bool ok = billing.ChangeStatus(bill.Number, BillStatus.Paid);

        Assert.False(ok);
        Assert.Equal(BillStatus.Draft, bill.Status);
    }

    [Fact]
    public void ChangeStatus_ОплаченийВВиданий_Заборонено()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing, issueNow: true);
        billing.ChangeStatus(bill.Number, BillStatus.Paid);

        bool ok = billing.ChangeStatus(bill.Number, BillStatus.Issued);

        Assert.False(ok);
        Assert.Equal(BillStatus.Paid, bill.Status);
    }

    [Fact]
    public void ChangeStatus_ВиданийВСкасований_Дозволено()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing, issueNow: true);

        bool ok = billing.ChangeStatus(bill.Number, BillStatus.Cancelled);

        Assert.True(ok);
        Assert.Equal(BillStatus.Cancelled, bill.Status);
    }

    [Fact]
    public void ChangeStatus_ПростроченийВОплачений_Дозволено()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing, issueNow: true);
        bill.Status = BillStatus.Overdue;

        bool ok = billing.ChangeStatus(bill.Number, BillStatus.Paid);

        Assert.True(ok);
        Assert.Equal(BillStatus.Paid, bill.Status);
    }

    [Fact]
    public void ChangeStatus_НеіснуючийРахунок_ПовертаєFalse()
    {
        var billing = NewService(out _);

        bool ok = billing.ChangeStatus(424242, BillStatus.Paid);

        Assert.False(ok);
    }

    [Fact]
    public void Pay_ЧастковаОплата_РахунокЛишаєтьсяВідкритим()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing, issueNow: true);

        bool ok = billing.Pay(bill.Number, 500);

        Assert.True(ok);
        Assert.Equal(500, bill.PaidSum, 6);
        Assert.Equal(BillStatus.Issued, bill.Status);
    }

    [Fact]
    public void Pay_ПовнаОплата_ЗакриваєРахунок()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing, issueNow: true);
        billing.Pay(bill.Number, 500);

        bool ok = billing.Pay(bill.Number, 368.80);

        Assert.True(ok);
        Assert.Equal(BillStatus.Paid, bill.Status);
        Assert.Equal(DateTime.Today, bill.PaidOn.Date);
    }

    [Fact]
    public void Pay_Чернетка_ОплатуВідхилено()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing);

        bool ok = billing.Pay(bill.Number, 100);

        Assert.False(ok);
        Assert.Equal(0, bill.PaidSum, 6);
    }

    [Fact]
    public void Pay_НевідємнаСума_ОплатуВідхилено()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing, issueNow: true);

        bool ok = billing.Pay(bill.Number, 0);

        Assert.False(ok);
    }

    [Fact]
    public void RefreshOverdue_МинулийТермін_ПозначаєПростроченим()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing, issueNow: true);
        bill.DueDate = DateTime.Today.AddDays(-3);

        billing.RefreshOverdue();

        Assert.Equal(BillStatus.Overdue, bill.Status);
        Assert.True(bill.Penalty > 0);
        Assert.Equal(2.61, bill.Penalty, 6);   // 868.80 * 0.001 * 3
    }

    [Fact]
    public void RefreshOverdue_ОплаченийРахунок_НеЧіпає()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing, issueNow: true);
        billing.ChangeStatus(bill.Number, BillStatus.Paid);
        bill.DueDate = DateTime.Today.AddDays(-30);

        billing.RefreshOverdue();

        Assert.Equal(BillStatus.Paid, bill.Status);
        Assert.Equal(0, bill.Penalty, 6);
    }

    [Fact]
    public void CorrectBill_Електрика110кВт_ПерераховуєЗПільгою()
    {
        // 100 * 2.64 + 10 * 4.32 = 307.20; УБД -25% -> 230.40.
        var billing = NewService(out var repo);
        var bill = billing.CreateBill("UB-1002", "EL", 8650, 8800, 0, Yesterday, true);

        bool ok = billing.CorrectBill(bill.Number, 110);

        Assert.True(ok);
        Assert.Equal(230.40, bill.Amount, 6);
        Assert.Equal(110, bill.Units, 6);
        Assert.Equal("corrected", bill.Note);
    }

    [Fact]
    public void CorrectBill_Вода_Відхиляється()
    {
        var billing = NewService(out _);
        var bill = billing.CreateBill("UB-1003", "WATER", 912, 930, 0, Yesterday, true);

        bool ok = billing.CorrectBill(bill.Number, 10);

        Assert.False(ok);
    }

    [Fact]
    public void CorrectBill_ОплаченийРахунок_Відхиляється()
    {
        var billing = NewService(out _);
        var bill = NewDraft(billing, issueNow: true);
        billing.ChangeStatus(bill.Number, BillStatus.Paid);

        bool ok = billing.CorrectBill(bill.Number, 110);

        Assert.False(ok);
    }

    [Fact]
    public void PrintForecast_250кВт_ПоказуєСумуБезПільги()
    {
        var billing = NewService(out var repo);
        var reports = new ReportService(repo);
        string output = CaptureConsole(() => reports.PrintForecast("UB-1002", 250));

        Assert.Contains("912", output);
        Assert.Contains("combat veteran -25% not included", output);
    }

    [Fact]
    public void PrintForecast_НевідомийРахунок_Повідомляє()
    {
        var billing = NewService(out var repo);
        var reports = new ReportService(repo);
        string output = CaptureConsole(() => reports.PrintForecast("UB-0000", 100));

        Assert.Contains("no account UB-0000", output);
    }

    [Fact]
    public void PrintBill_НевідомийРахунок_Повідомляє()
    {
        var billing = NewService(out var repo);
        var reports = new ReportService(repo);
        string output = CaptureConsole(() => reports.PrintBill(424242));

        Assert.Contains("not found", output);
    }

    private static string CaptureConsole(Action action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }
}
