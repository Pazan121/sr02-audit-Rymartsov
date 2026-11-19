# sr02-audit-Rymartsov

Самостійна робота № 2 «Аудит якості та рефакторинг успадкованого коду»,
дисципліна «Конструювання програмного забезпечення».

- Виконав: Римарцов Володимир, група 471, варіант 14 (UtilityBills).
- Джерело коду: А — пакет викладача `V14-UtilityBills` з репозиторію
  https://github.com/andreichenko256/legacy-code (без змін, тег `baseline`).
- Модуль-донор: `BillCalculator.Charge` — нарахування за електрику, газ і воду.
- Профільний дефект: сходинки тарифу на електрику продубльовані в `Charge`
  і `CorrectBill`, пільга — у `Charge` і `PrintForecast`; обов'язкові
  рефакторинги — заміна магічних чисел константами і декомпозиція умов.

## Структура

| Шлях | Що це |
|---|---|
| `src/` | код, що аудитується (консольний застосунок .NET 8) |
| `Legacy.Tests/` | характеризаційні тести (xUnit) |
| `tools/Metrics/` | вимірювач метрик за методикою М1–М3 (Roslyn) |
| `docs/metrics-before.csv`, `docs/metrics-after.csv` | метрики до і після |
| `docs/defects.md` | реєстр дефектів проєктування коду |
| `docs/plan.md` | план рефакторингу «вигода / ризик» |
| `docs/decisions.md` | журнал рішень |
| `docs/debt.md` | реєстр технічного боргу і план погашення |
| `docs/out-before.txt`, `docs/out-after.txt` | вивід демо-сценарію до і після |

Теги: `baseline` (`7c901a1`) — вихідний код; `safety-net` (`ba91c7a`) — сітка
тестів готова; `after-refactoring` (`296d723`) — усі рефакторинги виконано.

## Як запустити

```
dotnet test
dotnet run --project src
dotnet run --project tools/Metrics -- src docs/metrics-after.csv
```

Метрики до рефакторингу знімаються з тегу `baseline` через окреме робоче дерево:

```
git worktree add ..\baseline baseline
dotnet run --project tools/Metrics -- ..\baseline\src docs/metrics-before.csv
git worktree remove ..\baseline
```

Покриття: `dotnet test --collect:"XPlat Code Coverage"` (звіт Cobertura в `TestResults`).

Перевірка тих самих тестів до рефакторингу: `git checkout safety-net`, потім `dotnet test`
(43 Passed, як і на `after-refactoring`; повернутися — `git checkout master`).

## Результати

| Метрика | До | Після | Ціль |
|---|---|---|---|
| CC max | 21 | 7 | ≤ 8 |
| CC avg | 5.2 | 3.2 | ≤ 4 |
| Найдовший метод, лог. рядків | 53 | 29 | ≤ 40 |
| Вкладеність | 3 | 2 | ≤ 3 |
| Клони 6+ рядків | 2 | 0 | 0 |
| Покриття `src` | 73.2 % | 78.4 % | ≥ 60 % |
| Тестів | 43 | 43 | ≥ 10 |

Середня CC — за методами, довшими за п'ять логічних рядків (18 до і 21 після).

Незмінність поведінки: `docs/out-before.txt` і `docs/out-after.txt` побайтово
однакові (1971 байт), 43 тести зелені на обох тегах, покриття не впало.

## Публікація

```
git remote add origin https://github.com/<логін>/sr02-audit-Rymartsov.git
git push -u origin master --tags
```
