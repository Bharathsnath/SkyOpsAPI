using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.Interfaces;

namespace SkyOpsQueueIntelligence.Controllers;

[AllowAnonymous]
[ApiController]
public class PnrDetailsController : ControllerBase
{
    private readonly IQueueService _queueService;
    private readonly IEmailNotificationService _emailService;

    public PnrDetailsController(IQueueService queueService, IEmailNotificationService emailService)
    {
        _queueService = queueService;
        _emailService = emailService;
    }

    [HttpPost("/test-email")]
    public async Task<IActionResult> SendTestEmail(CancellationToken ct)
    {
        try
        {
            await _emailService.SendTestEmailAsync(ct);
            return Ok(new { message = "Test email sent successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Test email failed to send.", error = ex.Message });
        }
    }

    [HttpGet("/pnr-details/{pnr}")]
    public async Task<IActionResult> GetPnrDetailsPage(string pnr, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pnr))
            return BadRequest("PNR is required.");

        var data = await _queueService.GetDelayAnalysisByPnrAsync(pnr.ToUpperInvariant(), ct);
        if (data is null)
            return NotFound($"No data found for PNR: {pnr}");

        var html = BuildHtml(data);
        return Content(html, "text/html", Encoding.UTF8);
    }

    private static string BuildHtml(PnrDelayAnalysisDto data)
    {
        string pnr = data.Pnr;
        string receivedFrom = data.ReceivedFrom ?? "-";
        string agent = data.AgentCode ?? "-";
        string currencyCode = data.CurrencyCode ?? "INR";
        string deadline = data.TicketingDeadline ?? "-";
        var fare = data.FareSummary;
        var passengers = data.Passengers;
        var segList = data.Segments;

        var firstSeg = segList.Count > 0 ? segList[0] : null;
        string origin = firstSeg?.Origin ?? "-";
        string destination = firstSeg?.Destination ?? "-";
        string depTime = firstSeg?.DepartureTime ?? "-";
        string arrTime = firstSeg?.ArrivalTime ?? "-";
        string depDate = firstSeg?.DepartureDate ?? "";
        string flightCode = firstSeg?.FlightNo ?? "-";
        string statusCode = firstSeg?.StatusCode ?? "TK";
        int delayMinutes = firstSeg?.DelayMinutes ?? 0;

        var sb = new StringBuilder();

        sb.Append(@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8""/>
<meta name=""viewport"" content=""width=device-width,initial-scale=1.0""/>
<title>PNR Details — "); sb.Append(pnr); sb.Append(@"</title>
<script src=""https://cdn.tailwindcss.com""></script>
<link href=""https://fonts.googleapis.com/icon?family=Material+Icons+Round"" rel=""stylesheet""/>
<style>
  body { background: #edf3fb; }
  .tab-btn { border-bottom: 2px solid transparent; transition: all 0.3s; }
  .tab-btn.active { border-bottom-color: #2563eb; color: #2563eb; }
  .tab-content { display: none; }
  .tab-content.active { display: block; }
  .modal { display: none; position: fixed; inset: 0; z-index: 50; align-items: center; justify-content: center; background: rgba(15,23,42,0.5); backdrop-filter: blur(4px); }
  .modal.show { display: flex; }
  .modal-content { background: white; border-radius: 8px; box-shadow: 0 20px 25px -5px rgba(0,0,0,0.1); max-width: 768px; width: 100%; max-height: 90vh; overflow-y: auto; }
  .modal-sm { max-width: 448px; }
</style>
</head>
<body>
<section class=""mx-auto max-w-[1280px] px-4 py-5 md:px-8 md:py-7"">

  <!-- HEADER -->
  <header class=""mb-5 flex flex-col gap-4 rounded-[20px] border border-slate-200 bg-white px-4 py-4 shadow-sm md:flex-row md:items-center md:justify-between md:px-5"">
    <div class=""flex items-center gap-4"">
      <div>
        <nav class=""flex items-center gap-2 text-xs font-medium text-slate-500"">
          <span>PNR Queue</span>
          <span class=""material-icons-round text-[16px] text-slate-300"">chevron_right</span>
          <span class=""text-slate-800"">Workflow Details</span>
        </nav>
        <h1 class=""mt-1 text-2xl font-extrabold leading-tight text-slate-950"">PNR "); sb.Append(pnr); sb.Append(@"</h1>
      </div>
    </div>
    <div class=""flex items-center gap-2 rounded-full bg-slate-50 px-3 py-2 text-xs font-medium text-slate-500"">
      <span class=""material-icons-round text-[16px] text-slate-400"">schedule</span>
      "); sb.Append(DateTime.Now.ToString("dd MMM yyyy HH:mm")); sb.Append(@"
    </div>
  </header>

  <!-- MAIN CONTENT GRID -->
  <div class=""grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,1fr)_340px]"">
    <main class=""space-y-6"">

      <!-- PNR HERO CARD -->
      <article class=""overflow-hidden rounded-[24px] border border-slate-200 bg-white shadow-sm"">
        <div class=""border-b border-slate-100 px-5 py-5 md:px-6"">
          <div class=""flex flex-col gap-4 md:flex-row md:items-start md:justify-between"">
            <div class=""space-y-2"">
              <div class=""flex flex-wrap items-center gap-3"">
                <h2 class=""text-3xl font-extrabold leading-none text-slate-950"">"); sb.Append(pnr); sb.Append(@"</h2>
                <span class=""inline-flex items-center rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-[11px] font-semibold text-emerald-700"">"); sb.Append(GetTicketStatus(data.IsTicketed, data.CurrencyCode)); sb.Append(@"</span>
              </div>
              <p class=""text-sm text-slate-500"">
                Booked "); sb.Append(depDate); sb.Append(@"
                <span class=""mx-2 text-slate-300"">|</span>
                Agent "); sb.Append(agent); sb.Append(@"
              </p>
            </div>

            <!-- ALERT BOX -->
            <div class=""w-full max-w-[560px]"">
");
        if (delayMinutes > 0)
        {
            sb.Append(@"
              <div class=""rounded-2xl border border-amber-300 bg-[#fff9e8] px-4 py-3 text-amber-900"">
                <div class=""flex items-start gap-3"">
                  <span class=""material-icons-round mt-0.5 text-[20px] text-amber-500"">warning_amber</span>
                  <div>
                    <div class=""text-sm font-bold"">Time Change Queue</div>
                    <div class=""mt-1 text-xs leading-5 text-amber-800"">Delay: "); sb.Append(delayMinutes); sb.Append(@" min</div>
                  </div>
                </div>
              </div>");
        }
        sb.Append(@"
            </div>
          </div>
        </div>

        <!-- DEPARTURE / ARRIVAL SECTION -->
        <div class=""grid grid-cols-1 gap-4 px-4 py-5 md:grid-cols-[minmax(0,1fr)_148px_minmax(0,1fr)] md:gap-6 md:px-5 md:py-5"">
          <div class=""rounded-[18px] bg-[#f5f8fe] px-4 py-4 md:px-5 md:py-5"">
            <div class=""text-[11px] font-bold uppercase tracking-[.18em] text-slate-400"">Departure</div>
            <div class=""mt-2 text-4xl font-extrabold leading-none text-slate-950"">"); sb.Append(origin); sb.Append(@"</div>
            <div class=""mt-4 text-2xl font-bold text-slate-900"">"); sb.Append(depTime); sb.Append(@"</div>
            <div class=""mt-2 flex flex-wrap items-center gap-2"">
"); 
        if (delayMinutes > 0)
        {
            sb.Append(@"
              <span class=""inline-flex items-center gap-1 rounded-full bg-[#fff2da] px-2.5 py-1 text-xs font-semibold text-[#f97316]"">
                <span class=""material-icons-round text-[13px]"">schedule</span>
                "); sb.Append(delayMinutes); sb.Append(@" min delay
              </span>");
        }
        sb.Append(@"
              <span class=""text-xs font-medium text-slate-400"">"); sb.Append(depDate); sb.Append(@"</span>
            </div>
          </div>

          <div class=""flex flex-col items-center justify-center py-3 text-slate-400"">
            <div class=""grid h-12 w-12 place-items-center rounded-full bg-blue-600 shadow-[0_10px_20px_rgba(37,99,235,0.22)]"">
              <span class=""material-icons-round text-[20px] text-white"">flight</span>
            </div>
            <span class=""mt-3 rounded-full border border-blue-200 bg-blue-50 px-3 py-1 text-xs font-semibold text-blue-700"">"); sb.Append(flightCode); sb.Append(@"</span>
            <div class=""mt-3 h-px w-full bg-gradient-to-r from-transparent via-slate-300 to-transparent""></div>
            <div class=""mt-2 text-xs font-medium text-slate-400"">Direct</div>
          </div>

          <div class=""rounded-[18px] bg-[#f5f8fe] px-4 py-4 text-right md:px-5 md:py-5"">
            <div class=""text-[11px] font-bold uppercase tracking-[.18em] text-slate-400"">Arrival</div>
            <div class=""mt-2 text-4xl font-extrabold leading-none text-slate-950"">"); sb.Append(destination); sb.Append(@"</div>
            <div class=""mt-4 text-2xl font-bold text-slate-900"">"); sb.Append(arrTime); sb.Append(@"</div>
            <div class=""mt-2 flex flex-wrap items-center justify-end gap-2"">
");
        if (delayMinutes > 0)
        {
            sb.Append(@"
              <span class=""inline-flex items-center gap-1 rounded-full bg-[#fff2da] px-2.5 py-1 text-xs font-semibold text-[#f97316]"">
                <span class=""material-icons-round text-[13px]"">schedule</span>
                "); sb.Append(delayMinutes); sb.Append(@" min delay
              </span>");
        }
        sb.Append(@"
              <span class=""text-xs font-medium text-slate-400"">"); sb.Append(depDate); sb.Append(@"</span>
            </div>
          </div>
        </div>
      </article>

      <!-- SEGMENTS / PASSENGERS / REMARKS TABS -->
      <section class=""overflow-hidden rounded-[24px] border border-slate-200 bg-white shadow-sm"">
        <div class=""border-b border-slate-100 px-5 pt-4"">
          <div class=""flex items-center gap-3"">
            <button type=""button"" class=""tab-btn active border-b-2 px-4 pb-3 text-sm font-semibold text-blue-600"" onclick=""switchTab('segments')"">
              Flight Segments
            </button>
            <button type=""button"" class=""tab-btn border-b-2 px-4 pb-3 text-sm font-semibold text-slate-500"" onclick=""switchTab('passengers')"">
              Passengers
            </button>
            <button type=""button"" class=""tab-btn border-b-2 px-4 pb-3 text-sm font-semibold text-slate-500"" onclick=""switchTab('remarks')"">
              Remarks
            </button>
          </div>
        </div>

        <div class=""p-5 md:p-6"">
          <!-- SEGMENTS TAB -->
          <div id=""segments"" class=""tab-content active"">
            <div class=""space-y-4"">
");
        foreach (var seg in segList)
        {
            string sc = seg.StatusCode;
            string badgeClass = GetStatusBadgeClass(sc);
            sb.Append(@"
              <article class=""overflow-hidden rounded-[20px] border border-slate-200 bg-white shadow-[0_6px_20px_rgba(15,23,42,0.04)]"">
                <div class=""border-b border-slate-100 px-4 py-4 md:px-5"">
                  <div class=""flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between"">
                    <div class=""flex flex-wrap items-center gap-2"">
                      <div class=""text-base font-extrabold text-slate-950"">"); sb.Append(seg.FlightNo); sb.Append(@"</div>
                      <span class=""rounded-md bg-slate-100 px-2 py-1 text-xs font-medium text-slate-500"">Economy</span>
                      <span class=""rounded-full border border-blue-200 bg-blue-50 px-2.5 py-1 text-xs font-semibold text-blue-700"">"); sb.Append(GetStatusLabel(sc)); sb.Append(@"</span>
                    </div>

                    <div class=""flex items-center gap-4 text-right"">
                      <div>
                        <div class=""text-xs font-medium text-slate-500"">Departure</div>
                        <div class=""text-sm font-extrabold text-slate-950"">"); sb.Append(seg.Origin); sb.Append(@"</div>
                        <div class=""text-xs text-slate-500"">"); sb.Append(seg.DepartureTime); sb.Append(@" | "); sb.Append(seg.DepartureDate); sb.Append(@"</div>
                      </div>
                      <div class=""text-slate-400"">→</div>
                      <div>
                        <div class=""text-xs font-medium text-slate-500"">Arrival</div>
                        <div class=""text-sm font-extrabold text-slate-950"">"); sb.Append(seg.Destination); sb.Append(@"</div>
                        <div class=""text-xs text-slate-500"">"); sb.Append(seg.ArrivalTime); sb.Append(@" | "); sb.Append(seg.DepartureDate); sb.Append(@"</div>
                      </div>
                    </div>
                  </div>
                </div>

                <div class=""grid grid-cols-2 gap-x-4 gap-y-4 px-4 py-4 md:grid-cols-4 md:px-5"">
                  <div>
                    <div class=""text-xs text-slate-400"">Aircraft</div>
                    <div class=""mt-1 text-sm font-semibold text-slate-950"">-</div>
                  </div>
                  <div>
                    <div class=""text-xs text-slate-400"">Duration</div>
                    <div class=""mt-1 text-sm font-semibold text-slate-950"">2h 15m</div>
                  </div>
                  <div>
                    <div class=""text-xs text-slate-400"">Delay</div>
                    <div class=""mt-1 text-sm font-semibold text-orange-500"">"); sb.Append((seg.DelayMinutes ?? 0) > 0 ? seg.DelayMinutes + " min" : "0 min"); sb.Append(@"</div>
                  </div>
                  <div>
                    <div class=""text-xs text-slate-400"">Meal</div>
                    <div class=""mt-1 text-sm font-semibold text-slate-950"">-</div>
                  </div>
                </div>
              </article>");
        }
        sb.Append(@"
            </div>
          </div>

          <!-- PASSENGERS TAB -->
          <div id=""passengers"" class=""tab-content"">
            <div class=""space-y-3"">
");
        if (passengers is not null && passengers.Any())
        {
            foreach (var p in passengers)
            {
                string name = p.Name ?? "-";
                string initials = GetInitials(name);
                sb.Append(@"
              <div class=""flex items-center justify-between gap-4 rounded-[18px] border border-slate-200 bg-slate-50 px-4 py-4"">
                <div class=""flex items-center gap-3"">
                  <div class=""grid h-11 w-11 place-items-center rounded-full bg-blue-600 text-sm font-bold text-white"">"); sb.Append(initials); sb.Append(@"</div>
                  <div>
                    <div class=""text-sm font-bold text-slate-950"">"); sb.Append(name); sb.Append(@"</div>
                    <div class=""text-xs text-slate-500"">Adult | Seat "); sb.Append(p.Seat ?? "-"); sb.Append(@" | Meal "); sb.Append(p.Meal ?? "-"); sb.Append(@"</div>
                  </div>
                </div>
                <div class=""text-right text-xs text-slate-500"">
                  <div class=""font-semibold text-slate-800"">Confirmed</div>
                  <div>Passenger</div>
                </div>
              </div>");
            }
        }
        else
        {
            sb.Append(@"
              <div class=""rounded-[18px] border border-dashed border-slate-200 bg-slate-50 px-4 py-10 text-center text-sm text-slate-500"">
                No passengers available.
              </div>");
        }
        sb.Append(@"
            </div>
          </div>

          <!-- REMARKS TAB -->
          <div id=""remarks"" class=""tab-content"">
            <div class=""rounded-[20px] border border-slate-200 bg-slate-50 p-5 text-sm text-slate-700"">
              <div class=""text-xs font-bold uppercase tracking-[.16em] text-slate-400"">Remarks</div>
              <div class=""mt-3 whitespace-pre-wrap leading-6"">No remarks recorded.</div>
            </div>
          </div>
        </div>
      </section>
    </main>

    <!-- SIDEBAR -->
    <aside class=""space-y-4"">

      <!-- ACTIONS CARD -->
      <div class=""rounded-[24px] border border-slate-200 bg-white p-4 shadow-sm"">
        <div class=""mb-4 text-sm font-extrabold uppercase tracking-[.08em] text-slate-500"">Actions</div>
        <button class=""mb-3 flex w-full items-center justify-center gap-2 rounded-xl border border-red-200 bg-[#fff5f5] py-3 text-sm font-bold text-red-600 transition hover:bg-red-50"" type=""button"" onclick=""handleActionClick()"">
          <span class=""material-icons-round text-[16px]"">cancel</span>
          Cancel PNR
        </button>
        <button class=""mb-3 flex w-full items-center justify-center gap-2 rounded-xl border border-blue-600 bg-blue-600 py-3 text-sm font-bold text-white shadow-sm transition hover:bg-blue-700"" type=""button"" onclick=""handleActionClick()"">
          <span class=""material-icons-round text-[16px]"">event_repeat</span>
          Reschedule
        </button>
        <button class=""flex w-full items-center justify-center gap-2 rounded-xl border border-orange-200 bg-[#fff8f1] py-3 text-sm font-bold text-orange-600 transition hover:bg-orange-50"" type=""button"" onclick=""handleActionClick()"">
          <span class=""material-icons-round text-[16px]"">playlist_remove</span>
          Q Remove
        </button>
      </div>

      <!-- FARE SUMMARY -->
      <div class=""overflow-hidden rounded-[24px] border border-slate-200 bg-white shadow-sm"">
        <div class=""border-b border-slate-100 px-4 py-4"">
          <div class=""text-sm font-extrabold uppercase tracking-[.08em] text-slate-500"">Fare Summary</div>
        </div>
        <div class=""space-y-0 px-4 py-2"">
          <div class=""flex items-center justify-between border-b border-slate-100 py-3 text-sm"">
            <span class=""text-slate-500"">Base Fare</span>
            <span class=""font-semibold text-slate-950"">"); sb.Append(FormatMoney(fare.BaseFare, currencyCode)); sb.Append(@"</span>
          </div>
          <div class=""flex items-center justify-between border-b border-slate-100 py-3 text-sm"">
            <span class=""text-slate-500"">Taxes &amp; Fees</span>
            <span class=""font-semibold text-slate-950"">"); sb.Append(FormatMoney(fare.Taxes, currencyCode)); sb.Append(@"</span>
          </div>
          <div class=""flex items-end justify-between py-4"">
            <div>
              <div class=""text-sm font-bold text-slate-950"">Total</div>
              <div class=""text-xs text-slate-400"">"); sb.Append(currencyCode); sb.Append(@"</div>
            </div>
            <div class=""text-right"">
              <div class=""text-2xl font-extrabold tracking-tight text-slate-950"">"); sb.Append(FormatMoney(fare.Total, currencyCode)); sb.Append(@"</div>
              <div class=""text-xs text-slate-400"">"); sb.Append(currencyCode); sb.Append(@"</div>
            </div>
          </div>
        </div>
      </div>

      <!-- BOOKING INFO -->
      <div class=""overflow-hidden rounded-[24px] border border-slate-200 bg-white shadow-sm"">
        <div class=""border-b border-slate-100 px-4 py-4"">
          <div class=""text-sm font-extrabold uppercase tracking-[.08em] text-slate-500"">Booking Info</div>
        </div>
        <div class=""divide-y divide-slate-100 px-4"">
          <div class=""flex items-center justify-between py-3 text-sm"">
            <span class=""text-slate-500"">PNR</span>
            <span class=""font-semibold text-blue-700"">"); sb.Append(pnr); sb.Append(@"</span>
          </div>
          <div class=""flex items-center justify-between py-3 text-sm"">
            <span class=""text-slate-500"">Booking Date</span>
            <span class=""font-semibold text-slate-950"">"); sb.Append(depDate); sb.Append(@"</span>
          </div>
          <div class=""flex items-center justify-between py-3 text-sm"">
            <span class=""text-slate-500"">Ticketing Deadline</span>
            <span class=""font-semibold text-slate-950"">"); sb.Append(deadline); sb.Append(@"</span>
          </div>
          <div class=""flex items-center justify-between py-3 text-sm"">
            <span class=""text-slate-500"">Agent Code</span>
            <span class=""font-semibold text-slate-950"">"); sb.Append(agent); sb.Append(@"</span>
          </div>
          <div class=""flex items-center justify-between py-3 text-sm"">
            <span class=""text-slate-500"">Cabin</span>
            <span class=""font-semibold text-slate-950"">Economy</span>
          </div>
          <div class=""flex items-center justify-between py-3 text-sm"">
            <span class=""text-slate-500"">Passengers</span>
            <span class=""font-semibold text-slate-950"">"); sb.Append(passengers?.Count ?? 0); sb.Append(@" pax</span>
          </div>
          <div class=""flex items-center justify-between py-3 text-sm"">
            <span class=""text-slate-500"">Source</span>
            <span class=""font-semibold text-slate-950"">"); sb.Append(receivedFrom); sb.Append(@"</span>
          </div>
        </div>
      </div>

      <!-- NEED HELP -->
      <div class=""overflow-hidden rounded-[24px] border border-slate-200 bg-white shadow-sm"">
        <div class=""border-b border-slate-100 px-4 py-4"">
          <div class=""text-sm font-extrabold uppercase tracking-[.08em] text-slate-500"">Need Help?</div>
        </div>
        <div class=""space-y-3 px-4 py-4 text-sm text-slate-700"">
          <div class=""flex items-center gap-3"">
            <span class=""grid h-9 w-9 place-items-center rounded-full bg-slate-100 text-slate-500"">
              <span class=""material-icons-round text-[16px]"">call</span>
            </span>
            <span class=""font-medium"">+91 1800-123-4567</span>
          </div>
          <div class=""flex items-center gap-3"">
            <span class=""grid h-9 w-9 place-items-center rounded-full bg-slate-100 text-slate-500"">
              <span class=""material-icons-round text-[16px]"">mail</span>
            </span>
            <span class=""font-medium"">support@traveldesk.com</span>
          </div>
        </div>
      </div>
    </aside>
  </div>
</section>

<!-- Q REMOVE MODAL -->
<div id=""qRemoveModal"" class=""modal"">
  <div class=""modal-content modal-sm"">
    <div class=""flex items-start justify-between border-b border-slate-100 p-5"">
      <div>
        <div class=""text-lg font-extrabold text-slate-950"">Q Remove Segment</div>
        <div class=""mt-1 text-sm text-slate-500"">Remove cancelled queue segments from this PNR.</div>
      </div>
      <button class=""text-slate-400 hover:text-slate-600"" type=""button"" onclick=""closeModal('qRemoveModal')"">
        <span class=""material-icons-round text-[20px]"">close</span>
      </button>
    </div>
    <div class=""space-y-4 p-5"">
      <div>
        <label class=""block text-sm font-medium text-slate-700 mb-2"">Remarks</label>
        <textarea class=""w-full rounded-lg border border-slate-200 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-100"" rows=""3"" placeholder=""Additional remarks""></textarea>
      </div>
    </div>
    <div class=""flex items-center justify-end gap-3 border-t border-slate-100 bg-slate-50 p-4"">
      <button class=""rounded-lg border border-slate-200 bg-white px-4 py-2 text-sm font-bold text-slate-700 hover:bg-slate-100"" type=""button"" onclick=""closeModal('qRemoveModal')"">Cancel</button>
      <button class=""rounded-lg bg-red-600 px-4 py-2 text-sm font-bold text-white shadow-sm hover:bg-red-700"" type=""button"">Submit</button>
    </div>
  </div>
</div>

<!-- LOGIN MODAL -->
<div id=""loginModal"" class=""modal"">
  <div class=""modal-content modal-sm"">
    <div class=""flex items-start justify-between border-b border-slate-100 p-5"">
      <div>
        <div class=""text-lg font-extrabold text-slate-950"">Login Required</div>
        <div class=""mt-1 text-sm text-slate-500"">Please login to access delay analysis.</div>
      </div>
      <button class=""text-slate-400 hover:text-slate-600"" type=""button"" onclick=""closeModal('loginModal')"">
        <span class=""material-icons-round text-[20px]"">close</span>
      </button>
    </div>
    <div class=""space-y-4 p-5"">
      <div>
        <label class=""block text-sm font-medium text-slate-700 mb-2"">Username</label>
        <input id=""loginEmail"" type=""text"" class=""w-full rounded-lg border border-slate-200 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-100"" placeholder=""your_username"" />
      </div>
      <div>
        <label class=""block text-sm font-medium text-slate-700 mb-2"">Password</label>
        <input id=""loginPassword"" type=""password"" class=""w-full rounded-lg border border-slate-200 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-100"" placeholder=""••••••••"" />
      </div>
    </div>
    <div class=""flex items-center justify-end gap-3 border-t border-slate-100 bg-slate-50 p-4"">
      <button class=""rounded-lg border border-slate-200 bg-white px-4 py-2 text-sm font-bold text-slate-700 hover:bg-slate-100"" type=""button"" onclick=""closeModal('loginModal')"">Cancel</button>
      <button id=""loginBtn"" class=""rounded-lg bg-blue-600 px-4 py-2 text-sm font-bold text-white shadow-sm hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-blue-400"" type=""button"" onclick=""handleLogin()"">Login</button>
    </div>
  </div>
</div>

<script>
  const PNR = '"); 
        sb.Append(pnr);
        sb.Append(@"';

  function switchTab(tabName) {
    // Hide all tabs
    document.querySelectorAll('.tab-content').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.tab-btn').forEach(el => el.classList.remove('active', 'border-blue-600', 'text-blue-600'));
    
    // Show selected tab
    document.getElementById(tabName).classList.add('active');
    event.target.classList.add('active', 'border-blue-600', 'text-blue-600');
  }

  function openModal(modalId) {
    document.getElementById(modalId).classList.add('show');
  }

  function closeModal(modalId) {
    document.getElementById(modalId).classList.remove('show');
  }

  function handleActionClick() {
    const token = localStorage.getItem('auth_token');
    
    if (token) {
      // User is already logged in, redirect to delay-analysis
      window.location.href = 'https://skyopsbeta.akbartravelsonline.com/delay-analysis/' + PNR;
    } else {
      // User not logged in, show login modal
      openModal('loginModal');
    }
  }

  async function handleLogin() {
    const username = document.getElementById('loginEmail').value.trim();
    const password = document.getElementById('loginPassword').value.trim();

    if (!username || !password) {
      alert('Please enter both username and password');
      return;
    }

    const loginBtn = document.getElementById('loginBtn');
    loginBtn.disabled = true;
    loginBtn.textContent = 'Logging in...';

    try {
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          username: username,
          password: password
        })
      });

      const data = await response.json();

      if (response.ok && data.token) {
        // Save token to localStorage
        localStorage.setItem('auth_token', data.token);
        
        // Close login modal
        closeModal('loginModal');
        
        // Redirect to delay-analysis
        window.location.href = 'https://skyopsbeta.akbartravelsonline.com/delay-analysis/' + PNR;
      } else {
        alert(data.message || 'Login failed. Please try again.');
        loginBtn.disabled = false;
        loginBtn.textContent = 'Login';
      }
    } catch (error) {
      alert('An error occurred: ' + error.message);
      loginBtn.disabled = false;
      loginBtn.textContent = 'Login';
    }
  }

  // Allow Enter key to submit login
  document.addEventListener('keypress', function(event) {
    if (event.key === 'Enter') {
      const loginModal = document.getElementById('loginModal');
      if (loginModal && loginModal.classList.contains('show')) {
        handleLogin();
      }
    }
  });
</script>

</body>
</html>");

        return sb.ToString();
    }

    private static string GetStatusBadgeClass(string status) => status.ToUpperInvariant() switch
    {
        "HX" => "bg-red-50 text-red-700 ring-1 ring-red-100",
        "UN" or "UC" => "bg-orange-50 text-orange-700 ring-1 ring-orange-100",
        "TK" => "bg-emerald-50 text-emerald-700 ring-1 ring-emerald-100",
        "WL" => "bg-blue-50 text-blue-700 ring-1 ring-blue-100",
        _ => "bg-slate-50 text-slate-700 ring-1 ring-slate-200"
    };

    private static string GetStatusLabel(string status) => status.ToUpperInvariant() switch
    {
        "HX" => "Cancelled",
        "UN" or "UC" => "Unavailable",
        "TK" => "Time Changed",
        "WL" => "Waitlisted",
        _ => status
    };

    private static string GetTicketStatus(bool isTicketed, string? currencyCode)
    {
        return isTicketed || !string.IsNullOrWhiteSpace(currencyCode)
            ? "Ticketed"
            : "Unticketed";
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ');
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[parts.Length - 1][0]}".ToUpperInvariant();
        return name.Substring(0, Math.Min(2, name.Length)).ToUpperInvariant();
    }

    private static string FormatMoney(decimal? value, string currencyCode)
    {
        if (value is null) return "-";
        return currencyCode.ToUpperInvariant() switch
        {
            "INR" => $"\u20b9{value:N0}",
            "USD" => $"USD {value:N0}",
            "EUR" => $"EUR {value:N0}",
            "GBP" => $"GBP {value:N0}",
            _ => $"{currencyCode} {value:N0}"
        };
    }
}
