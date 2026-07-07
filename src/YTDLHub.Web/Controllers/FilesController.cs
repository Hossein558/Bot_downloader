using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YTDLHub.Core.Interfaces;
using YTDLHub.Core.Models;
using YTDLHub.Infrastructure.Options;
using System.IO;
using System.Threading.Tasks;
using YTDLHub.Infrastructure.Data;
using System.Linq;

namespace YTDLHub.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IDownloadService _downloadService;
    private readonly AppDbContext _db;
    private readonly YtDlpOptions _opts;
    private readonly ILogger<FilesController> _logger;

    public FilesController(IDownloadService downloadService, AppDbContext db, IOptions<YtDlpOptions> opts, ILogger<FilesController> logger)
    {
        _downloadService = downloadService;
        _db = db;
        _opts = opts.Value;
        _logger = logger;
    }

    [HttpGet("download/{jobId}")]
    public IActionResult DownloadFile(Guid jobId)
    {
        var job = _downloadService.GetJob(jobId);
        
        // If not in memory, it might be an older job stored in DB
        if (job == null)
        {
            job = _db.DownloadJobs.FirstOrDefault(j => j.Id == jobId);
        }

        if (job == null)
        {
            return NotFound("Job not found or has been cleaned up.");
        }

        if (job.Status != YTDLHub.Core.Enums.JobStatus.Completed || string.IsNullOrEmpty(job.FilePath))
        {
            return BadRequest("File is not ready for download.");
        }

        if (!System.IO.File.Exists(job.FilePath))
        {
            return NotFound("File not found on disk. It might have been deleted.");
        }

        var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(job.FilePath, out string? contentType))
        {
            contentType = "application/octet-stream";
        }

        var fileStream = new FileStream(job.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        
        // Return a FileStreamResult for chunked downloading and memory efficiency
        return File(fileStream, contentType, job.FileName, enableRangeProcessing: true);
    }
}
