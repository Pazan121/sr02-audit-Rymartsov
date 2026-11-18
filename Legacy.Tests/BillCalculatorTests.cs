using System;
using UtilityBills;
using Xunit;

namespace Legacy.Tests;

/// <summary>
/// Характеризаційні тести розрахункового вузла BillCalculator.Charge.
/// Фіксують наявну поведінку, зокрема помилкову (див. TD-01 у реєстрі боргу).
/// </summary>
public class BillCalculatorTests
{
    private static readonly DateTime Today = new DateTime(2026, 11, 17);
    private static readonly DateTime Yesterday = Today.AddDays(-1);

    private static BillCalculator NewCalculator(out BillingRepository repo)
    {
        repo = new BillingRepository();
        return new BillCalculator(repo);
    }

    // R11: тести збирають той самий ChargeRequest, що й робочий код
    private static double Charge(BillCalculator calc, string acc, string svc, double prev,
        double cur, double night, DateTime readAt, DateTime? today = null)
    {
        return calc.Charge(new ChargeRequest(acc, svc, prev, cur, night, readAt)
        {
            Today = today
        });
    }

    [Fact]
    public void Charge_80кВт_Повертає211_20()
    {
        // arrange
        var calc = NewCalculator(out _);

        // act
        double actual = Charge(calc, "UB-1001", "EL", 0, 80, 0, Yesterday, Today);

        // assert
        Assert.Equal(211.20, actual, 6);
        Assert.Equal(80, calc.LastUnits, 6);
        Assert.Equal(0, calc.LastSubsidy, 6);
    }

    [Fact]
    public void Charge_РівноЗначення100кВт_Повертає264()
    {
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1001", "EL", 0, 100, 0, Yesterday, Today);

        Assert.Equal(264.00, actual, 6);
    }

    [Fact]
    public void Charge_350кВт_Повертає1344()
    {
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1001", "EL", 0, 350, 0, Yesterday, Today);

        Assert.Equal(1344.00, actual, 6);
    }

    [Fact]
    public void Charge_600кВт_Повертає2424()
    {
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1001", "EL", 0, 600, 0, Yesterday, Today);

        Assert.Equal(2424.00, actual, 6);
    }

    [Fact]
    public void Charge_601кВт_Повертає2424_ПомилкаТретьоїСходинки()
    {
        // Характеризаційний тест. Третя сходинка рахує (u - 601) замість
        // (u - 600), тому 601 кВт дає ту саму суму, що й 600 кВт.
        // Це зафіксовано як наявна поведінка; див. TD-01 у реєстрі боргу.
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1001", "EL", 0, 601, 0, Yesterday, Today);

        Assert.Equal(2424.00, actual, 6);
    }

    [Fact]
    public void Charge_750кВт_Повертає3347_80()
    {
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1001", "EL", 0, 750, 0, Yesterday, Today);

        Assert.Equal(3347.80, actual, 6);
    }

    [Fact]
    public void Charge_НульовеСпоживання_ПовертаєНуль()
    {
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1001", "EL", 500, 500, 0, Yesterday, Today);

        Assert.Equal(0, actual, 6);
    }

    [Fact]
    public void Charge_Газ45м3_ДодаєАбонплату()
    {
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1001", "GAS", 3410, 3455, 0, Yesterday, Today);

        Assert.Equal(395.70, actual, 6);
    }

    [Fact]
    public void Charge_Вода18м3_Повертає928_80()
    {
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1001", "WATER", 912, 930, 0, Yesterday, Today);

        Assert.Equal(928.80, actual, 6);
    }

    [Fact]
    public void Charge_ЧАЕС_ЗнижкаПлюсСубсидія_Повертає250()
    {
        // 300 кВт: 264 + 200 * 4.32 = 1128; нічна зона 120 кВт -> -225.60;
        // пільга ЧАЕС -50% -> 451.20; субсидія обрізає до 250.
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1005", "EL", 7700, 8000, 120, Yesterday, Today);

        Assert.Equal(250.00, actual, 6);
        Assert.Equal(201.20, calc.LastSubsidy, 6);
    }

    [Fact]
    public void Charge_УчасникБойовихДій_Знижка25_Повертає360()
    {
        // 150 кВт: 264 + 50 * 4.32 = 480; -25% -> 360.
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1002", "EL", 8650, 8800, 0, Yesterday, Today);

        Assert.Equal(360.00, actual, 6);
    }

    [Fact]
    public void Charge_Багатодітна_Знижка25_СубсидіяОбрізає600()
    {
        // Вода 18 м3: 928.80; -25% -> 696.60; субсидія 600 -> 600.
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1003", "WATER", 912, 930, 0, Yesterday, Today);

        Assert.Equal(600.00, actual, 6);
        Assert.Equal(96.60, calc.LastSubsidy, 6);
    }

    [Fact]
    public void Charge_ЗастарілийПоказник_РахуєЗаНормою()
    {
        // Показник старший за 45 днів: 1 мешканець -> норма 70 кВт;
        // 70 * 2.64 = 184.80; УБД -25% -> 138.60.
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1002", "EL", 8650, 9100, 0,
            Today.AddDays(-60), Today);

        Assert.Equal(138.60, actual, 6);
        Assert.Equal(70, calc.LastUnits, 6);
        Assert.Equal("by norm", calc.LastNote);
    }

    [Fact]
    public void Charge_ЕлектроОпалення_Листопад_Перші3000ЗаПершимСтупенем()
    {
        // 3000 * 2.64 + 2000 * 4.32 = 7920 + 8640 = 16560.
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1004", "EL", 0, 5000, 0,
            Yesterday, new DateTime(2026, 11, 17));

        Assert.Equal(16560.00, actual, 6);
    }

    [Fact]
    public void Charge_ЕлектроОпалення_Липень_РахуєЗаЗвичайнимиСходинками()
    {
        // Поза сезоном звичайні сходинки: 2424 + 4399 * 6.20 = 29697.80.
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1004", "EL", 0, 5000, 0,
            new DateTime(2026, 7, 15).AddDays(-1), new DateTime(2026, 7, 15));

        Assert.Equal(29697.80, actual, 6);
    }

    [Fact]
    public void Charge_НевідомийРахунок_ПовертаєМінус1()
    {
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-9999", "EL", 0, 100, 0, Yesterday, Today);

        Assert.Equal(-1, actual, 6);
    }

    [Fact]
    public void Charge_ВідємнийПоказник_ПовертаєМінус2()
    {
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1001", "EL", 100, -5, 0, Yesterday, Today);

        Assert.Equal(-2, actual, 6);
    }

    [Fact]
    public void Charge_НевідомаПослуга_РахуєЗаТарифомВоди()
    {
        // Характеризаційний тест. Невідомий код послуги потрапляє в гілку
        // else і рахується як вода: 10 м3 * (30.12 + 21.48) = 516.
        // Це зафіксовано як наявна поведінка; див. TD-04 у реєстрі боргу.
        var calc = NewCalculator(out _);

        double actual = Charge(calc, "UB-1001", "HEAT", 0, 10, 0, Yesterday, Today);

        Assert.Equal(516.00, actual, 6);
    }

    [Fact]
    public void NormUnits_ТриМешканці_НормаЕлектрики130()
    {
        var calc = NewCalculator(out _);

        Assert.Equal(130, calc.NormUnits("EL", 3), 6);
    }

    [Fact]
    public void NormUnits_ДваМешканці_НормаГазу19_6()
    {
        var calc = NewCalculator(out _);

        Assert.Equal(19.60, calc.NormUnits("GAS", 2), 6);
    }

    [Fact]
    public void NormUnits_ДваМешканці_НормаВоди4_8()
    {
        var calc = NewCalculator(out _);

        Assert.Equal(4.80, calc.NormUnits("WATER", 2), 6);
    }
}
