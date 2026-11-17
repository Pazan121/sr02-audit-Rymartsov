namespace UtilityBills;

/// <summary>
/// Тариф на електрику: сходинки, електроопалення, нічний тариф.
/// Виділено з BillCalculator (R09), щоб правило тарифу мало одне місце.
/// </summary>
public static class ElectricityTariff
{
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

    /// <summary>Опалювальний сезон: жовтень-квітень.</summary>
    public static bool IsHeatingSeason(DateTime moment)
        => moment.Month >= 10 || moment.Month <= 4;

    /// <summary>Вартість за сходинками: до 100 - 2.64, 100-600 - 4.32, далі 6.20.</summary>
    public static double Calculate(double units)
    {
        if (units <= FirstStepLimit)
            return units * FirstStepRate;
        if (units <= SecondStepLimit)
            return FirstStepLimit * FirstStepRate
                + (units - FirstStepLimit) * SecondStepRate;
        return FirstStepLimit * FirstStepRate
            + ThirdStepBase * SecondStepRate
            + (units - ThirdStepOffset) * ThirdStepRate;
    }

    /// <summary>Електроопалення: до 3000 кВт*год - за першим ступенем.</summary>
    public static double CalculateHeating(double units)
    {
        if (units <= HeatingStepLimit)
            return units * FirstStepRate;
        return HeatingStepLimit * FirstStepRate
            + (units - HeatingStepLimit) * SecondStepRate;
    }

    /// <summary>Двозонний лічильник: нічні кіловати за пів ціни.</summary>
    public static double ApplyNightMeter(double amount, double nightReading, double units)
    {
        double nightPart = amount * nightReading / units;
        return amount - nightPart * NightMeterDiscount;
    }
}

/// <summary>Тариф на газ: ставка за м3 плюс абонплата за розподіл.</summary>
public static class GasTariff
{
    private const double Rate = 7.96;
    private const double DistributionFee = 37.50;

    public static double Calculate(double units)
    {
        double gas = units * Rate;
        return gas + DistributionFee;
    }
}

/// <summary>Тариф на воду: водопостачання плюс водовідведення.</summary>
public static class WaterTariff
{
    private const double ColdWaterRate = 30.12;
    private const double SewageRate = 21.48;

    public static double Calculate(double units)
    {
        double cold = units * ColdWaterRate;
        double drain = units * SewageRate;
        return cold + drain;
    }
}

/// <summary>Норми споживання на місяць, якщо показник не передали.</summary>
public static class ConsumptionNorms
{
    private const double ElectricityBase = 70;
    private const double ElectricityPerResident = 30;
    private const double GasPerResident = 9.8;
    private const double WaterPerResident = 2.4;

    public static double For(string service, int residents)
    {
        if (service == "EL")
            return ElectricityBase + ElectricityPerResident * (residents - 1);
        if (service == "GAS")
            return GasPerResident * residents;
        return WaterPerResident * residents;   // вода
    }
}
