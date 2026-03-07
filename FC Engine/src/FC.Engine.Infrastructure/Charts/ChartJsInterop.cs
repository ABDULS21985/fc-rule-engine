using FC.Engine.Domain.Models;
using Microsoft.JSInterop;

namespace FC.Engine.Infrastructure.Charts;

public class ChartJsInterop
{
    private readonly IJSRuntime _js;

    public ChartJsInterop(IJSRuntime js)
    {
        _js = js;
    }

    public Task RenderLineChart(string canvasId, TrendData data)
        => _js.InvokeVoidAsync("renderChart", canvasId, "line", data).AsTask();

    public Task RenderBarChart(string canvasId, TrendData data)
        => _js.InvokeVoidAsync("renderChart", canvasId, "bar", data).AsTask();

    public Task RenderDoughnutChart(string canvasId, TrendData data)
        => _js.InvokeVoidAsync("renderChart", canvasId, "doughnut", data).AsTask();

    public Task RenderAreaChart(string canvasId, string[] labels, int[] accepted, int[] rejected, int[] pending)
        => _js.InvokeVoidAsync("renderAreaChart", canvasId, labels, accepted, rejected, pending).AsTask();

    public Task RenderHorizontalBarChart(string canvasId, string[] labels, int[] values, decimal[] compliance, object? dotNetRef = null)
        => _js.InvokeVoidAsync("renderHorizontalBarChart", canvasId, labels, values, compliance, dotNetRef).AsTask();

    public Task RenderDonutWithColors(string canvasId, string[] labels, decimal[] values, string[] colors, object? dotNetRef = null)
        => _js.InvokeVoidAsync("renderDonutWithColors", canvasId, labels, values, colors, dotNetRef).AsTask();

    public Task RenderHeatmap(string canvasId, object days)
        => _js.InvokeVoidAsync("renderHeatmap", canvasId, days).AsTask();

    public Task AnimateCounter(string elementId, decimal targetValue, int durationMs, string suffix = "")
        => _js.InvokeVoidAsync("animateCounter", elementId, targetValue, durationMs, suffix).AsTask();

    public Task InitDragDrop(DotNetObjectReference<object> dotNetRef)
        => _js.InvokeVoidAsync("initDragDrop", dotNetRef).AsTask();

    public Task RenderStaggered(string[] widgetIds, int delayMs = 100)
        => _js.InvokeVoidAsync("renderStaggered", widgetIds, delayMs).AsTask();

    public ValueTask<string?> GetDashboardWidgetOrder()
        => _js.InvokeAsync<string?>("getDashboardWidgetOrder");
}
