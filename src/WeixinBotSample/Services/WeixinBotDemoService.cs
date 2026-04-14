using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using QRCoder;
using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService(
    IHttpClientFactory httpClientFactory,
    JsonStateStore stateStore,
    FixedGreetingService fixedGreetingService,
    IWebHostEnvironment environment,
    ILogger<WeixinBotDemoService> logger)
{
}
