# План і журнал рефакторингу

## 1. Мета і межі

- Об'єкт аудиту: модуль нарахувань `BillCalculator.Charge` (варіант 14) у застосунку
  UtilityBills — рахунки за електрику, газ і воду для ЖБК.
- Джерело: `V14-UtilityBills` (пакет A), скопійований без змін у `src/`.
  Власний семестровий проєкт не використовується — методичка забороняє аудит власного коду.
- Мета: знизити складність і дублювання, не змінивши жодної спостережуваної поведінки.

## 2. Цільові метрики

| Метрика | Ціль | До | Після | Статус |
|---|---|---|---|---|
| Максимальна CC | ≤ 8 | 21 | 7 | досягнуто |
| Середня CC | ≤ 4 | 5.2 | 3.2 | досягнуто |
| Найдовший метод, лог. рядків | ≤ 40 | 53 | 29 | досягнуто |
| Максимальна вкладеність | ≤ 3 | 3 | 2 | досягнуто |
| Клони 6+ рядків | 0 | 2 групи | 0 | досягнуто |
| Покриття рядків `src` | ≥ 60 % | 73.2 % | 78.4 % | досягнуто |
| Тестів зелених | ≥ 10 | 43 | 43 | досягнуто |

## 3. Етапи

### Етап 0. Підготовка репозиторію

- Створено `sr02-audit-Rymartsov`, скопійовано `src/`, додано `.gitignore`, `Legacy.sln`,
  тестовий проєкт `Legacy.Tests` (xUnit + coverlet).
- Коміт `7c901a1` → тег **`baseline`**.

### Етап 1. Фіксація поведінки

- `283b1fa` — знято еталонний вивід сценарію `docs/out-before.txt`
  і початкові метрики `docs/metrics-before.csv` (методика М1–М3).
- `9bb109a` — шов для годинника: необов'язковий `DateTime? today` у `Charge`
  (поведінка за замовчуванням не змінюється).
- `ba91c7a` — 43 характеризаційні тести, покриття `BillCalculator.cs` 96.6 %,
  загалом 73.2 % → тег **`safety-net`**.
- Мутаційна перевірка сітки: заміна `2.64 → 2.65` дала 13 падінь, після чого зміну відкотили.

### Етап 2. Рефакторинг (один коміт — одна техніка, після кожного `dotnet test` зелений)

| № | Техніка (каталог R01–R20) | Що зроблено | Коміт |
|---|---|---|---|
| 1 | R02 Rename | `calcPenalty → CalcPenalty`, `accNo/svc/u/p → accountNumber/serviceCode/units/penalty` | `5a40857` |
| 2 | R04 Magic Numbers | тарифи й пеня — іменовані константи | `cf8b234` |
| 3 | R03 Explaining Variable | `isStaleReading`, `isHeatingSeason`, `isNightMeter` | `6153d6a` |
| 4 | R01 Extract Method | `CalculateElectricity`, `ApplyBenefit` | `e9d2274` |
| 5 | R05 Decompose Conditional | сходинки тарифу — окремі методи | `4bda3e0` |
| 6 | R06 Guard Clause | перевірка рахунку в звіті замість вкладеного `try/catch` | `b469771` |
| 7 | R09 Extract Class | `ElectricityTariff`, `GasTariff`, `WaterTariff`, `ConsumptionNorms` (`src/Tariffs.cs`) | `0ad512f` |
| 8 | R10 Move Method | розрахунок сходинок і нічного тарифу — в `ElectricityTariff` | `f31a2b9` |
| 9 | R08 Replace Conditional with Table | таблиця `AllowedTransitions` у `ChangeStatus` | `ae8d2da` |
| 10 | R13 Encapsulate Field/Collection | приватні списки, `IReadOnlyList`, `AddBill`, `private set` | `6b47630` |
| 11 | R15 Replace Temp with Query | `b.ToPay() <= 0` замість `PaidSum >= Amount + Penalty` | `8c0d095` |
| 12 | R16 Split Loop | `MarkOverdueBills` + `RecalculatePenalties` | `fc2b02a` |
| 13 | R17 Remove Dead Code | `SmsEnabled`, закоментована гілка `HEAT` | `2c3a675` |
| 14 | R19 Remove Flag Argument | `CreateDraftBill` / `CreateIssuedBill` | `97a9f4f` |
| 15 | R11 Introduce Parameter Object | `ChargeRequest` + конструктор | `b59e061`, `3c1a488` |
| 16 | R12 Replace Primitive with Value Object | `BenefitCategory` (код, назва, знижка) | `f543e18` |

Додатково: `3189fee` — завершення R05 (`CalculateBase`, `ApplySubsidy`), щоб `Charge`
досяг CC ≤ 8; `296d723` — маркери боргу `TD-01…TD-08` у коментарях (поведінка не змінюється).

**Разом:** 16 різних технік, 18 рефакторингових комітів (вимога ≥ 12).
Обов'язкові для варіанта 14 — **R04** і **R05** — виконано (`cf8b234`, `4bda3e0`, `3189fee`).

### Етап 3. Доказ незмінної поведінки

1. `docs/out-before.txt` і `docs/out-after.txt` — побайтово однакові
   (`git diff --no-index` не показав різниці; 1971 байт обидва).
2. 43 тести зелені на `safety-net` і на `after-refactoring`; очікування в тестах
   не змінювалися — лише сигнатури на кроках R19/R11.
3. Покриття після рефакторингу не впало: `BillCalculator.cs` 96.6 % → 97.5 %, `src` 73.2 % → 78.4 %.

Тег **`after-refactoring`** — коміт `296d723`.

### Етап 4. Артефакти

- `docs/metrics-before.csv`, `docs/metrics-after.csv` — метрики до/після,
  зняті утилітою `tools/Metrics` (C#, Roslyn).
- `docs/defects.md` — 14 карток дефектів (12 типів).
- `docs/decisions.md` — журнал рішень.
- `docs/debt.md` — реєстр технічного боргу з оцінками PERT.

## 4. Порядок відтворення

```powershell
git worktree add ..\baseline baseline
dotnet run --project tools/Metrics -- ..\baseline\src docs/metrics-before.csv
git worktree remove ..\baseline

dotnet run --project tools/Metrics -- src docs/metrics-after.csv
dotnet test
dotnet run --project src | Out-File -Encoding utf8 docs\out-after.txt
```
