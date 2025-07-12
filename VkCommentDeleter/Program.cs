using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Html.Dom;
using Spectre.Console;

var httpClient = new HttpClient();
var commentUrlRegex =
    new Regex(@"https://vk\.com/wall(-?\d+)_(\d+)\?reply=(\d+)(?:&thread=(\d+))?", RegexOptions.Compiled);

AnsiConsole.Write(
    new FigletText("VK Comment Deleter")
        .LeftJustified()
        .Color(Color.Blue));

AnsiConsole.WriteLine();

var accessToken = AnsiConsole.Prompt(
    new TextPrompt<string>("Введите access token VK API:")
        .Secret());

var archivePath = AnsiConsole.Prompt(
    new TextPrompt<string>("Введите путь к распакованному архиву ВК данных:")
        .Validate(path =>
        {
            if (!Directory.Exists(path))
                return ValidationResult.Error("Директория не существует");

            var commentsPath = Path.Combine(path, "comments");
            if (!Directory.Exists(commentsPath))
                return ValidationResult.Error("Папка 'comments' не найдена в указанной директории");

            return ValidationResult.Success();
        }));

var commentsPath = Path.Combine(archivePath, "comments");

var htmlFiles = Directory.GetFiles(commentsPath, "*.html", SearchOption.AllDirectories);

AnsiConsole.MarkupLine($"[green]Найдено {htmlFiles.Length} HTML файлов[/]");

if (htmlFiles.Length == 0)
{
    AnsiConsole.MarkupLine("[red]HTML файлы не найдены![/]");
    return;
}

var confirm = AnsiConsole.Confirm("Вы уверены, что хотите удалить все найденные комментарии?");
if (!confirm)
{
    AnsiConsole.MarkupLine("[yellow]Операция отменена[/]");
    return;
}

await ProcessHtmlFiles(htmlFiles);
return;

async Task ProcessHtmlFiles(string[] htmls)
{
    var totalDeleted = 0;
    var totalFailed = 0;
    var totalProcessed = 0;

    await AnsiConsole.Progress()
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("[green]Обработка HTML файлов[/]");
            task.MaxValue = htmls.Length;

            foreach (var htmlFile in htmls)
            {
                AnsiConsole.MarkupLine($"[blue]Обрабатывается: {Path.GetFileName(htmlFile)}[/]");

                try
                {
                    var (deleted, failed, processed) = await ProcessSingleHtmlFile(htmlFile);
                    totalDeleted += deleted;
                    totalFailed += failed;
                    totalProcessed += processed;

                    AnsiConsole.MarkupLine($"[green]Файл обработан: {processed} комментариев найдено, " +
                        $"{deleted} удалено, {failed} ошибок[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Ошибка обработки файла " +
                        $"{Path.GetFileName(htmlFile)}: {ex.Message}[/]");
                }

                task.Increment(1);
            }
        });

    AnsiConsole.WriteLine();
    var table = new Table();
    table.AddColumn("Статистика");
    table.AddColumn("Значение");
    table.AddRow("Всего обработано файлов", htmls.Length.ToString());
    table.AddRow("Всего найдено комментариев", totalProcessed.ToString());
    table.AddRow("[green]Успешно удалено[/]", totalDeleted.ToString());
    table.AddRow("[red]Ошибок удаления[/]", totalFailed.ToString());

    AnsiConsole.Write(table);
}

async Task<(int deleted, int failed, int processed)> ProcessSingleHtmlFile(string htmlFile)
{
    var config = Configuration.Default;
    var context = BrowsingContext.New(config);

    var content = await File.ReadAllTextAsync(htmlFile);
    var document = await context.OpenAsync(req => req.Content(content));

    var items = document.QuerySelectorAll(".item");
    var comments = new List<CommentData>();
    var processed = 0;

    foreach (var item in items)
    {
        var linkElement = item.QuerySelector("a[href*='vk.com/wall']") as IHtmlAnchorElement;
        if (linkElement?.Href == null) continue;

        processed++;

        var match = commentUrlRegex.Match(linkElement.Href);
        if (!match.Success)
        {
            AnsiConsole.MarkupLine($"[yellow]Неверный формат ссылки: {linkElement.Href}[/]");
            continue;
        }

        var ownerId = match.Groups[1].Value;
        var commentId = match.Groups[3].Value;

        var commentData = new CommentData(ownerId, commentId);
        comments.Add(commentData);
    }

    var deleted = 0;
    var failed = 0;
    var batches = comments.Chunk(25);

    foreach (var batch in batches)
    {
        var (batchDeleted, batchFailed) = await DeleteCommentsBatch(batch);
        deleted += batchDeleted;
        failed += batchFailed;

        await Task.Delay(350);
    }

    return (deleted, failed, processed);
}

async Task<(int deleted, int failed)> DeleteCommentsBatch(CommentData[] batch)
{
    try
    {
        var code = $@"
var comments = {JsonSerializer.Serialize(batch.Select(c => new { owner_id = c.OwnerId, comment_id = c.CommentId }))};
var results = [];
var i = 0;
while (i < comments.length) {{
    var result = API.wall.deleteComment({{
        owner_id: parseInt(comments[i].owner_id),
        comment_id: parseInt(comments[i].comment_id)
    }});
    results.push({{
        comment_id: comments[i].comment_id,
        success: parseInt(result) == 1
    }});
    i = i + 1;
}}
return results;";

        var url = $"https://api.vk.com/method/execute?" +
            $"code={Uri.EscapeDataString(code)}&access_token={accessToken}&v=5.131";

        var response = await httpClient.GetAsync(url);
        var jsonResponse = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(jsonResponse);
        var root = document.RootElement;

        if (root.TryGetProperty("error", out var errorElement))
        {
            var errorCode = errorElement.GetProperty("error_code").GetInt32();
            var errorMsg = errorElement.GetProperty("error_msg").GetString();

            AnsiConsole.MarkupLine($"[red]Ошибка API VK (код {errorCode}): {errorMsg}[/]");
            return (0, batch.Length);
        }

        if (root.TryGetProperty("response", out var responseElement))
        {
            var deleted = 0;
            var failed = 0;

            foreach (var result in responseElement.EnumerateArray())
            {
                var commentId = result.GetProperty("comment_id").GetString();
                var success = result.GetProperty("success").GetBoolean();

                if (success)
                {
                    deleted++;
                    AnsiConsole.MarkupLine($"[green]✓ Удален комментарий {commentId}[/]");
                }
                else
                {
                    failed++;
                    AnsiConsole.MarkupLine($"[red]✗ Не удалось удалить комментарий {commentId}[/]");
                }
            }

            return (deleted, failed);
        }

        return (0, batch.Length);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Исключение при удалении батча: {ex.Message}[/]");
        return (0, batch.Length);
    }
}

internal record CommentData(string OwnerId, string CommentId);
