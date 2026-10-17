---
title: "ChurchServe Angular Tutorial Part 11: Calendar Filters, Query Parameters, and Pagination"
excerpt: "Create a shareable ChurchServe schedule whose month, ministry, and page live in the URL and drive a filtered, paginated ASP.NET Core API query."
category: "Angular Tutorial"

seo:
  focusKeyword: "Angular query parameters pagination tutorial"
  description: "Build Angular calendar filters and server pagination with typed query state, URL synchronization, SQLite filtering, and router tests."
  socialTitle: "ChurchServe Part 11: URL-Driven Calendar Filters"
  socialDescription: "Make schedule filters refresh-safe and shareable with typed Angular query parameters and tested server-side pagination."
---

# ChurchServe Angular Tutorial Part 11: Calendar Filters, Query Parameters, and Pagination

Part 10 added a cancellable volunteer search. The schedule still loads one unfiltered collection, though. That works for two seeded events, but it does not give a coordinator a durable way to view one month, narrow the results to a ministry, or move through a larger result set.

This part turns the schedule URL into an explicit view-state contract. A URL such as `/schedule?month=2026-11&ministry=technical&page=1` can be bookmarked, shared, refreshed, and revisited with the browser Back button. The API applies the same contract to SQLite and returns page metadata with the events.

> **Quick answer:** Parse query parameters into a validated `ScheduleQuery`, derive repository requests from router changes with `switchMap`, and let filter controls navigate instead of mutating private component state. On the server, validate paging input, filter before counting, sort deterministically, and return `items`, `page`, `pageSize`, and `totalItems`.

## What We Are Building

Part 11 changes both sides of the application:

- The Schedule page gains month and ministry controls.
- `month`, `ministry`, and `page` are stored in the URL.
- A typed repository query becomes HTTP query parameters.
- The API filters and pages in SQLite rather than in Angular.
- The response includes enough metadata to enable Previous and Next correctly.
- Loading, error, empty, and ready states continue to be distinct.
- Parser, HTTP, router, and API integration tests protect the contract.

The relevant structure after this part is:

```text
server/ChurchServe.Api/
  Data/
    ChurchServeDbContext.cs
    DatabaseInitializer.cs
  ServiceEvents/
    ServiceEvent.cs
    ServiceEventEndpoints.cs
    ServiceEventMinistry.cs
    ServiceEventPageResponse.cs
tests/ChurchServe.Api.Tests/
  ServiceEventEndpointsTests.cs
src/app/features/schedule/
  api/
    http-service-event-repository.ts
    service-event-dto.ts
  page.ts
  schedule-query.ts
  schedule.html
  schedule.scss
  schedule.ts
  service-event-repository.ts
```

There is no new Angular route. The existing `/schedule` route carries optional query parameters after the `?`.

## Define the Page and Query Contracts

Pagination is not an array with a few unrelated numbers beside it. Give the response a reusable type in `page.ts`:

```typescript
export interface Page<T> {
  readonly items: readonly T[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalItems: number;
}
```

Then add `schedule-query.ts`:

```typescript
import { ParamMap } from '@angular/router';

export const SCHEDULE_PAGE_SIZE = 4;

export const scheduleMinistries = [
  { value: 'welcome', label: 'Welcome' },
  { value: 'worship', label: 'Worship' },
  { value: 'technical', label: 'Technical' },
] as const;

export interface ScheduleQuery {
  readonly month: string;
  readonly ministry: string | null;
  readonly page: number;
  readonly pageSize: number;
}

export function parseScheduleQuery(params: ParamMap, now = new Date()): ScheduleQuery {
  const requestedMonth = params.get('month');
  const requestedMinistry = params.get('ministry')?.toLowerCase() ?? null;
  const requestedPage = Number(params.get('page'));

  return {
    month: isMonth(requestedMonth) ? requestedMonth : toMonth(now),
    ministry: scheduleMinistries.some(({ value }) => value === requestedMinistry)
      ? requestedMinistry
      : null,
    page: Number.isInteger(requestedPage) && requestedPage > 0 ? requestedPage : 1,
    pageSize: SCHEDULE_PAGE_SIZE,
  };
}

export function equalScheduleQuery(left: ScheduleQuery, right: ScheduleQuery): boolean {
  return (
    left.month === right.month &&
    left.ministry === right.ministry &&
    left.page === right.page &&
    left.pageSize === right.pageSize
  );
}

function isMonth(value: string | null): value is string {
  return value !== null && /^\d{4}-(0[1-9]|1[0-2])$/.test(value);
}

function toMonth(value: Date): string {
  const year = value.getFullYear();
  const month = String(value.getMonth() + 1).padStart(2, '0');
  return `${year}-${month}`;
}
```

Query parameters are untrusted strings. The parser accepts only a real `YYYY-MM` shape, one of the ministries that the UI supports, and a positive integer page. Missing or invalid values become safe defaults.

Passing `now` as an optional argument makes the date-dependent default deterministic in tests. `pageSize` does not come from the URL in this UI; keeping it fixed prevents a visitor from requesting an arbitrarily large page.

## Extend the Repository Boundary

Change `ServiceEventRepository` so callers must provide a complete query and receive a complete page:

```typescript
import { Observable } from 'rxjs';
import { CreateServiceEventCommand } from './create-service-event-command';
import { Page } from './page';
import { ScheduleQuery } from './schedule-query';
import { ServiceEvent } from './service-event';

export abstract class ServiceEventRepository {
  abstract list(query: ScheduleQuery): Observable<Page<ServiceEvent>>;
  abstract findById(id: string): Observable<ServiceEvent | undefined>;
  abstract create(command: CreateServiceEventCommand): Observable<ServiceEvent>;
}
```

The HTTP adapter converts the query to `HttpParams`:

```typescript
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, map, Observable, of, throwError } from 'rxjs';
import { CreateServiceEventCommand } from '../create-service-event-command';
import { Page } from '../page';
import { ScheduleQuery } from '../schedule-query';
import { ServiceEventRepository } from '../service-event-repository';
import { ServiceEvent } from '../service-event';
import { parseServiceEvent, parseServiceEventPage } from './service-event-dto';

@Injectable()
export class HttpServiceEventRepository extends ServiceEventRepository {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/service-events';

  list(query: ScheduleQuery): Observable<Page<ServiceEvent>> {
    let params = new HttpParams()
      .set('month', query.month)
      .set('page', query.page)
      .set('pageSize', query.pageSize);

    if (query.ministry) {
      params = params.set('ministry', query.ministry);
    }

    return this.http.get<unknown>(this.baseUrl, { params }).pipe(map(parseServiceEventPage));
  }

  findById(id: string): Observable<ServiceEvent | undefined> {
    return this.http.get<unknown>(`${this.baseUrl}/${encodeURIComponent(id)}`).pipe(
      map(parseServiceEvent),
      catchError((error: unknown) =>
        error instanceof HttpErrorResponse && error.status === 404
          ? of(undefined)
          : throwError(() => error),
      ),
    );
  }

  create(command: CreateServiceEventCommand): Observable<ServiceEvent> {
    return this.http.post<unknown>(this.baseUrl, command).pipe(map(parseServiceEvent));
  }
}
```

`HttpParams` handles encoding and makes optional parameters explicit. The response is still requested as `unknown`; TypeScript interfaces do not validate JSON at runtime.

Add page validation beside the existing event DTO parser:

```typescript
export function parseServiceEventPage(value: unknown): Page<ServiceEvent> {
  if (
    !isRecord(value) ||
    !Array.isArray(value['items']) ||
    !isPositiveInteger(value['page']) ||
    !isPositiveInteger(value['pageSize']) ||
    !isNonNegativeInteger(value['totalItems'])
  ) {
    throw new Error('The service-event page response did not match the expected contract.');
  }

  return {
    items: value['items'].map(parseServiceEvent),
    page: value['page'],
    pageSize: value['pageSize'],
    totalItems: value['totalItems'],
  };
}

function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0;
}

function isPositiveInteger(value: unknown): value is number {
  return isNonNegativeInteger(value) && value > 0;
}
```

Notice the type-predicate return values. They prove to strict TypeScript that the three metadata fields are numbers after validation.

## Model Which Ministries Need Each Event

Volunteer eligibility from Part 10 answers which ministries a person may serve. Schedule filtering needs a separate fact: which ministries are needed by an event.

Add `ServiceEventMinistry.cs`:

```csharp
namespace ChurchServe.Api.ServiceEvents;

public sealed class ServiceEventMinistry
{
  public required string ServiceEventId { get; init; }
  public required string Ministry { get; init; }
  public ServiceEvent ServiceEvent { get; init; } = null!;
}
```

Add the collection to `ServiceEvent`:

```csharp
public ICollection<ServiceEventMinistry> Ministries { get; } = [];
```

Configure the composite key and relationship in `ChurchServeDbContext.OnModelCreating`:

```csharp
var serviceEventMinistry = modelBuilder.Entity<ServiceEventMinistry>();
serviceEventMinistry.ToTable("ServiceEventMinistries");
serviceEventMinistry.HasKey(item => new { item.ServiceEventId, item.Ministry });
serviceEventMinistry.Property(item => item.ServiceEventId).HasMaxLength(64);
serviceEventMinistry.Property(item => item.Ministry).HasMaxLength(100);
serviceEventMinistry.HasOne(item => item.ServiceEvent)
    .WithMany(item => item.Ministries)
    .HasForeignKey(item => item.ServiceEventId)
    .OnDelete(DeleteBehavior.Cascade);
```

The two-column key prevents duplicate ministry tags on one event. Cascade delete prevents orphaned join rows.

This tutorial still uses the idempotent local-database upgrade approach introduced in Part 10. `DatabaseInitializer` creates `ServiceEventMinistries` only when it is missing and seeds mappings only when the table is empty:

```csharp
await dbContext.Database.ExecuteSqlRawAsync("""
    CREATE TABLE IF NOT EXISTS "ServiceEventMinistries" (
      "ServiceEventId" TEXT NOT NULL,
      "Ministry" TEXT NOT NULL,
      CONSTRAINT "PK_ServiceEventMinistries" PRIMARY KEY ("ServiceEventId", "Ministry"),
      CONSTRAINT "FK_ServiceEventMinistries_ServiceEvents_ServiceEventId"
        FOREIGN KEY ("ServiceEventId") REFERENCES "ServiceEvents" ("Id") ON DELETE CASCADE
    );
    """);

if (!await dbContext.ServiceEventMinistries.AnyAsync())
{
  dbContext.ServiceEventMinistries.AddRange(
      CreateServiceEventMinistry("sunday-2026-11-01", "welcome"),
      CreateServiceEventMinistry("sunday-2026-11-01", "worship"),
      CreateServiceEventMinistry("rehearsal-2026-11-05", "technical"),
      CreateServiceEventMinistry("rehearsal-2026-11-05", "worship"));
}
```

This preserves the existing `churchserve.db`. A production application should use versioned EF Core migrations rather than growing an initializer indefinitely.

## Keep SQLite Date Queries Translatable

SQLite's EF Core provider does not support ordering and range comparisons directly over `DateTimeOffset`. Part 11 stores the event's instant as a UTC `DateTime` in the entity while retaining `DateTimeOffset` at the HTTP boundary:

```csharp
public DateTime StartsAt { get; init; }
public DateTime EndsAt { get; init; }
```

Creation converts the offset-aware request to UTC:

```csharp
StartsAt = request.StartsAt.UtcDateTime,
EndsAt = request.EndsAt.UtcDateTime,
```

The response converts stored values back to an explicit UTC offset. This avoids provider-side client evaluation and keeps filtering, sorting, counting, and paging in SQL. It also makes the storage rule clear: persist instants in UTC; choose a display time zone at the presentation boundary.

## Return a Page from the API

Add `ServiceEventPageResponse.cs`:

```csharp
namespace ChurchServe.Api.ServiceEvents;

public sealed record ServiceEventPageResponse(
    IReadOnlyList<ServiceEventResponse> Items,
    int Page,
    int PageSize,
    int TotalItems);
```

Replace the list endpoint with a filtered query:

```csharp
group.MapGet("/", async (
    string? month,
    string? ministry,
    int? page,
    int? pageSize,
    ChurchServeDbContext dbContext,
    CancellationToken cancellationToken) =>
{
  var requestedPage = page.GetValueOrDefault(1);
  var requestedPageSize = pageSize.GetValueOrDefault(4);
  var errors = ValidatePaging(requestedPage, requestedPageSize);
  if (errors.Count > 0)
  {
    return Results.ValidationProblem(errors);
  }

  var query = dbContext.ServiceEvents.AsNoTracking();

  if (!string.IsNullOrWhiteSpace(month))
  {
    if (!TryGetMonthRange(month, out var monthStart, out var monthEnd))
    {
      return Results.ValidationProblem(new Dictionary<string, string[]>
      {
        ["month"] = ["Month must use YYYY-MM format."],
      });
    }

    query = query.Where(item => item.StartsAt >= monthStart && item.StartsAt < monthEnd);
  }

  var normalizedMinistry = ministry?.Trim().ToLowerInvariant();
  if (!string.IsNullOrEmpty(normalizedMinistry))
  {
    query = query.Where(item =>
        item.Ministries.Any(value => value.Ministry == normalizedMinistry));
  }

  var totalItems = await query.CountAsync(cancellationToken);
  var serviceEvents = await query
      .OrderBy(item => item.StartsAt)
      .ThenBy(item => item.Id)
      .Skip((requestedPage - 1) * requestedPageSize)
      .Take(requestedPageSize)
      .ToArrayAsync(cancellationToken);

  return Results.Ok(new ServiceEventPageResponse(
      serviceEvents.Select(ToResponse).ToArray(),
      requestedPage,
      requestedPageSize,
      totalItems));
});
```

Supporting validation caps the page size at 50:

```csharp
private static Dictionary<string, string[]> ValidatePaging(int page, int pageSize)
{
  var errors = new Dictionary<string, string[]>();
  if (page < 1)
  {
    errors["page"] = ["Page must be at least 1."];
  }

  if (pageSize is < 1 or > 50)
  {
    errors["pageSize"] = ["Page size must be between 1 and 50."];
  }

  return errors;
}

private static bool TryGetMonthRange(
    string month,
    out DateTime monthStart,
    out DateTime monthEnd)
{
  var parsed = DateTime.TryParseExact(
      $"{month}-01",
      "yyyy-MM-dd",
      CultureInfo.InvariantCulture,
      DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
      out monthStart);
  monthEnd = parsed ? monthStart.AddMonths(1) : default;
  return parsed;
}
```

The order of operations matters:

1. Validate the request.
2. Apply filters.
3. Count the filtered set.
4. Apply a deterministic order.
5. Skip and take one page.

`ThenBy(item => item.Id)` is not decorative. If two events start at the same instant, it prevents them from changing relative order between requests.

## Drive Loading from Router Changes

The Schedule component now has one request pipeline. Router changes and manual retries both produce a `ScheduleQuery`, and `switchMap` replaces stale work:

```typescript
const queryChanges$ = this.route.queryParamMap.pipe(
  map((params) => parseScheduleQuery(params)),
  distinctUntilChanged(equalScheduleQuery),
  tap((query) => this.query.set(query)),
);

merge(queryChanges$, this.retry.pipe(map(() => this.query())))
  .pipe(
    switchMap((query) =>
      this.repository.list(query).pipe(
        map((page): ScheduleState => ({ status: 'ready', page })),
        startWith<ScheduleState>({ status: 'loading' }),
        catchError(() =>
          of<ScheduleState>({
            status: 'error',
            message: 'The schedule could not be loaded. Check your connection and try again.',
          }),
        ),
      ),
    ),
    takeUntilDestroyed(this.destroyRef),
  )
  .subscribe((state) => this.state.set(state));
```

The component state carries the page, not a second copy of its metadata:

```typescript
export type ScheduleState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'ready'; page: Page<ServiceEvent> };

protected readonly events = computed(() => this.page()?.items ?? []);
protected readonly totalPages = computed(() => {
  const page = this.page();
  return page ? Math.max(1, Math.ceil(page.totalItems / page.pageSize)) : 1;
});
protected readonly hasPreviousPage = computed(() => this.query().page > 1);
protected readonly hasNextPage = computed(() => this.query().page < this.totalPages());
```

Filter methods navigate rather than calling the repository directly:

```typescript
protected changeMonth(month: string): void {
  void this.navigate({ month, page: 1 });
}

protected changeMinistry(ministry: string): void {
  void this.navigate({ ministry: ministry || null, page: 1 });
}

protected changePage(page: number): void {
  if (page >= 1 && page <= this.totalPages()) {
    void this.navigate({ page });
  }
}

private navigate(queryParams: Record<string, string | number | null>): Promise<boolean> {
  return this.router.navigate([], {
    relativeTo: this.route,
    queryParams,
    queryParamsHandling: 'merge',
  });
}
```

Changing a filter resets `page` to 1 because the old page may not exist in the narrower result set. `queryParamsHandling: 'merge'` preserves the other schedule settings.

The component also replaces missing or invalid initial values with the parsed canonical values. `replaceUrl: true` avoids adding a cleanup-only navigation to browser history. This means the URL visible to the user describes the view actually being rendered.

## Add Accessible Filter and Paging Controls

The important template additions are ordinary labeled form controls and a navigation landmark:

```html
<section class="schedule-filters" aria-labelledby="filter-heading">
  <h2 id="filter-heading">Filter schedule</h2>

  <label>
    Month
    <input
      type="month"
      [value]="query().month"
      (change)="changeMonth($any($event.target).value)"
    />
  </label>

  <label>
    Ministry
    <select [value]="query().ministry ?? ''" (change)="changeMinistry($any($event.target).value)">
      <option value="">All ministries</option>
      @for (ministry of ministries; track ministry.value) {
        <option [value]="ministry.value">{{ ministry.label }}</option>
      }
    </select>
  </label>
</section>
```

Render paging only after a successful response:

```html
@if (page(); as currentPage) {
  <nav class="pagination" aria-label="Schedule pages">
    <button
      type="button"
      [disabled]="!hasPreviousPage()"
      (click)="changePage(query().page - 1)"
    >
      Previous
    </button>
    <p aria-live="polite">
      Page {{ currentPage.page }} of {{ totalPages() }} · {{ currentPage.totalItems }} results
    </p>
    <button
      type="button"
      [disabled]="!hasNextPage()"
      (click)="changePage(query().page + 1)"
    >
      Next
    </button>
  </nav>
}
```

Disabled buttons communicate page boundaries, while `aria-live="polite"` announces the new position after navigation. On narrow screens, the CSS wraps the controls and places the page summary on its own row instead of shrinking everything into an unreadable strip.

## Test the Serialized Contract

Start with the pure query parser:

```typescript
it('replaces invalid values with safe defaults', () => {
  const now = new Date(2026, 9, 17);
  const query = parseScheduleQuery(
    convertToParamMap({ month: 'November', ministry: 'unknown', page: '-1' }),
    now,
  );

  expect(query).toEqual({ month: '2026-10', ministry: null, page: 1, pageSize: 4 });
});
```

The HTTP repository test verifies encoded request parameters and response metadata together:

```typescript
const result = firstValueFrom(
  repository.list({ month: '2026-11', ministry: 'technical', page: 2, pageSize: 4 }),
);

const request = http.expectOne(
  '/api/service-events?month=2026-11&page=2&pageSize=4&ministry=technical',
);
request.flush({ items: [dto], page: 2, pageSize: 4, totalItems: 5 });

await expect(result).resolves.toEqual({
  items: [expect.objectContaining({ id: 'event-1' })],
  page: 2,
  pageSize: 4,
  totalItems: 5,
});
```

The component test checks behavior at the router boundary:

```typescript
it('writes a ministry filter to the URL and resets the page', async () => {
  const router = TestBed.inject(Router);
  await router.navigate([], { queryParams: { month: '2026-11', page: 3 } });
  const fixture = TestBed.createComponent(Schedule);
  fixture.detectChanges();
  await fixture.whenStable();

  const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
  select.value = 'technical';
  select.dispatchEvent(new Event('change'));
  fixture.detectChanges();
  await fixture.whenStable();

  expect(router.url).toContain('month=2026-11');
  expect(router.url).toContain('ministry=technical');
  expect(router.url).toContain('page=1');
});
```

API integration tests cover the other half of the contract:

```csharp
[Fact]
public async Task List_filters_by_month_and_ministry()
{
  using var factory = new ChurchServeApiFactory();
  using var client = factory.CreateClient();

  var page = await client.GetFromJsonAsync<ServiceEventPageResponse>(
      "/api/service-events?month=2026-11&ministry=technical&page=1&pageSize=4");

  Assert.NotNull(page);
  Assert.Equal(1, page.TotalItems);
  Assert.Equal("rehearsal-2026-11-05", Assert.Single(page.Items).Id);
}
```

There are separate tests for stable page-two metadata and for rejecting a malformed month, page zero, and a page size above the server cap.

## Run and Verify Part 11

Start the API and Angular application in separate terminals:

```bash
dotnet run --project server/ChurchServe.Api
npm start
```

Open `http://localhost:4201/schedule`. The URL is canonicalized with the current month and page 1. To see the tutorial seed data, choose November 2026. Selecting Technical narrows the result to the worship-team rehearsal.

Run the complete checks:

```bash
dotnet test ChurchServe.slnx -c Release
npm test -- --watch=false
npm run build
```

Part 11 passes 14 API integration tests and 50 Angular tests. The production build also validates strict TypeScript and Angular template compilation.

## Design Result

The schedule is now reproducible from its URL, friendly to refresh and browser navigation, and able to grow beyond one in-memory collection. Angular owns parsing and navigation, the repository owns transport mapping, and the API owns filtering and paging over persisted data.

Part 12 can now focus on reusable presentation and content projection without hiding data-access or routing decisions inside generic UI components.
