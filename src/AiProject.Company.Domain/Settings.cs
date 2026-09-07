namespace AiProject.Company.Domain;

public sealed class CompanySettings : IMarginThresholds
{
    public static Guid SingletonId { get; } = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    public Guid Id { get; private set; } = SingletonId;
    public string CompanyName { get; private set; } = "";
    public string TimeZoneId { get; private set; } = "Asia/Taipei";
    public string Currency { get; private set; } = "TWD";
    public decimal YellowPercent { get; private set; } = 25m;
    public decimal RedPercent { get; private set; } = 10m;
    public bool WriteBackGithubAssignee { get; private set; } = true;
    public bool SetupCompleted { get; private set; }
    public List<ExchangeRate> ExchangeRates { get; private set; } = [];

    public static CompanySettings CreateDefault() => new();

    public void Update(string companyName, string timeZoneId, string currency, decimal yellowPercent, decimal redPercent, bool writeBack)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            throw new DomainException(ErrorCodes.Required, Messages.Required("公司名"));
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new DomainException(ErrorCodes.Required, Messages.Required("時區"));
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException(ErrorCodes.Required, Messages.Required("貨幣"));
        if (yellowPercent < redPercent)
            throw new DomainException(ErrorCodes.InvalidState, "黃色門檻必須高於紅色門檻。");
        CompanyName = companyName.Trim();
        TimeZoneId = timeZoneId.Trim();
        Currency = Currencies.Normalize(currency);
        YellowPercent = yellowPercent;
        RedPercent = redPercent;
        WriteBackGithubAssignee = writeBack;
        SetupCompleted = true;
    }

    public void SetExchangeRate(string currency, decimal rateToCompany, DateOnly asOf)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException(ErrorCodes.Required, Messages.Required("幣別"));
        if (rateToCompany <= 0)
            throw new DomainException(ErrorCodes.InvalidState, "匯率必須大於 0。");
        var code = Currencies.Normalize(currency);
        if (code == Currency)
            throw new DomainException(ErrorCodes.InvalidState, $"記帳貨幣 {Currency} 不需要匯率，固定為 1。");
        var existing = ExchangeRates.FirstOrDefault(x => x.Currency == code);
        if (existing is null)
            ExchangeRates.Add(new ExchangeRate(code, rateToCompany, asOf));
        else
            existing.Replace(rateToCompany, asOf);
    }

    public decimal ToCompanyCurrency(decimal amount, string currency)
    {
        var code = Currencies.Normalize(currency);
        if (code == Currency)
            return amount;
        var rate = ExchangeRates.FirstOrDefault(x => x.Currency == code)
            ?? throw new DomainException(ErrorCodes.NotFound, $"尚未設定 {code} 的匯率。");
        return decimal.Round(amount * rate.RateToCompany, 2);
    }

    public string Classify(decimal? marginPercent)
    {
        if (marginPercent is null)
            return "—";
        if (marginPercent.Value < RedPercent)
            return "紅";
        if (marginPercent.Value < YellowPercent)
            return "黃";
        return "綠";
    }
}

public sealed class ExchangeRate
{
    public string Currency { get; private set; }
    public decimal RateToCompany { get; private set; }
    public DateOnly AsOf { get; private set; }

    public ExchangeRate(string currency, decimal rateToCompany, DateOnly asOf)
    {
        Currency = currency;
        RateToCompany = rateToCompany;
        AsOf = asOf;
    }

    internal void Replace(decimal rateToCompany, DateOnly asOf)
    {
        RateToCompany = rateToCompany;
        AsOf = asOf;
    }
}
