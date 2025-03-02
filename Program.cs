using LLama.Native;
using System.Diagnostics;

namespace TrippinEdi; //Stumbling... upon... edification. Trippin' Edi. :)

internal static class Program
{
    private static readonly IDiscoveryService _discoveryService = new DiscoveryService();
    private static bool _pendingInvalidated;
    private static float _temperature;
    private static OutputHandler backgroundOutputHandler = new(true);

    //To support background generation while the user is doing other things
    private static readonly SemaphoreSlim _generationLock = new(1, 1);
    private static Task? _backgroundGeneration;

    static async Task Main(string[] args)
    {
        // Initialize database if not exists
        var dbContext = new DiscoveryContext(); //Cannot be initialized statically because it's ThreadStatic
        dbContext.Database.EnsureCreated();

        var latestInterestCreated = dbContext.Interests.Max<Interest, DateTime?>(q => q.CreatedAt) ?? DateTime.Now;
        var latestDislikeCreated = dbContext.Dislikes.Max<Dislike, DateTime?>(q => q.CreatedAt) ?? DateTime.Now;
        _pendingInvalidated = dbContext.PendingDiscoveries.Any(p => p.CreatedAt < latestInterestCreated || p.CreatedAt < latestDislikeCreated);

        //Keep llama.cpp from spamming the console since it's a console app. But save the log to a file in case it crashes and we need to change something.
        NativeLogConfig.llama_log_set(new FileLogger("llamacpp_log.txt"));

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;

            Console.WriteLine("\nOptions:");
            Console.WriteLine(" 0. How it works");
            Console.WriteLine(" 1. Add an interest");
            Console.WriteLine(" 2. Add a dislike");
            Console.WriteLine(" 3. Get a new fact/topic");
            Console.WriteLine(" 4. Generate another batch of facts/topics in the background");
            Console.WriteLine(" 5. Dump prompt so you can go ask a smarter LLM");
            Console.WriteLine(" X. Exit");

            Console.ForegroundColor = ConsoleColor.Gray;
            var choice = Console.ReadKey().KeyChar;
            Console.WriteLine();
            Console.ResetColor();

            switch (choice)
            {
                case '0':
                    Console.WriteLine("\nHow Trippin' Edi works:");
                    Console.WriteLine("------------------------");
                    Console.WriteLine("1. If you don't already have a decent large language model in GGUF format that is small enough to run on your computer, download one now and drop it in this program's working directory. I recommend this one if your VRAM + RAM totals at least 32 GB: https://huggingface.co/bartowski/FuseO1-DeepSeekR1-QwQ-SkyT1-Flash-32B-Preview-GGUF/blob/main/FuseO1-DeepSeekR1-QwQ-SkyT1-Flash-32B-Preview-Q5_K_M.gguf");
                    Console.WriteLine("2. While you're downloading the model, add some interests (topics you want to learn about) and dislikes (topics to avoid). You may get better results if you're a bit specific, e.g., 'programming (C#, C, Arduino, optimization)' instead of just 'programming.'");
                    Console.WriteLine("3. When you select 'Get a new fact/topic', the program will generate interesting facts based on your preferences if it doesn't already have some in the backlog. You may do something else for 10 minutes (depending on your hardware and the model size) while you wait.");
                    Console.WriteLine("4. During generation, you'll see various progress messages in gray text. This is normal, and it's meant to show you progress; you're not expected to read it.");
                    Console.WriteLine("5. The fact/topic you requested will appear in green text--just one. Press 3 again to get another. This allows you to spend some time fact-checking and researching, and you can close the program to pick up where you left off later without having to regenerate the facts.");
                    Console.WriteLine("6. Each fact is checked against your dislikes and previous discoveries to ensure uniqueness and relevance.");
                    Console.WriteLine("Note: These models aren't perfect. There's a decent chance of them missing the point entirely or failing to follow the instructions that allow the facts to be parsed from the generated text.");
                    Console.WriteLine("If that happens, just keep tapping 3 until it generates again.");
                    Console.WriteLine("If you're an expert, though, you might try deleting the unwanted non-facts from the PendingDiscoveries table in the SQLite database. However, also note that this app normally uses greedy sampling--given the exact same inputs, the results should be exactly the same--so you may want to add a fact to the database yourself to avoid getting the same garbage result again.");
                    Console.WriteLine("If you're about to be away from the computer for a while, you can use the background generation to prepare more facts before you start asking to see more.");

                    break;
                case '1':
                    AddInterest(dbContext);
                    break;
                case '2':
                    AddDislike(dbContext);
                    break;
                case '3':
                    await GetNewFact(dbContext);
                    break;
                case '4':
                    if (_backgroundGeneration?.IsCompleted != false)
                    {
                        backgroundOutputHandler = new(true); //Reset to logging to a file
                        await GenerateNewDiscoveries(dbContext, backgroundOutputHandler);
                        Console.WriteLine("Background generation started.");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Background generation already in progress. Select 3 to wait for it instead.");
                        Console.ResetColor();
                    }
                    break;
                case '5':
                    PrintInferPrompt(dbContext);
                    break;
                case 'x':
                case 'X':
                case '\u001b': //Escape
                    return;
                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Type the desired option number (0-5).");
                    Console.ResetColor();
                    break;
            }
        }
    }

    private static void AddInterest(DiscoveryContext dbContext)
    {
        Console.WriteLine("Enter interest:");
        var interest = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(interest))
        {
            dbContext.Interests.Add(new Interest
            {
                Name = interest,
                CreatedAt = DateTime.UtcNow
            });
            dbContext.SaveChanges();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Interest added successfully.");
            Console.ResetColor();
            _pendingInvalidated = true;
        }
    }

    private static void AddDislike(DiscoveryContext dbContext)
    {
        Console.WriteLine("Enter dislike:");
        var dislike = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(dislike))
        {
            Debug.Assert(dbContext != null);
            dbContext.Dislikes.Add(new Dislike
            {
                Name = dislike,
                CreatedAt = DateTime.UtcNow
            });
            dbContext.SaveChanges();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Dislike added successfully.");
            Console.ResetColor();
            _pendingInvalidated = true;
        }
    }

    private static async Task GetNewFact(DiscoveryContext dbContext)
    {
        if (_pendingInvalidated)
        {
            await ReEvaluatePendingDiscoveries(dbContext);
            _pendingInvalidated = false;
        }

        var pending = new List<PendingDiscovery>();
        while (pending.Count == 0)
        {
            pending = dbContext.PendingDiscoveries.ToList();
            if (pending.Count == 0)
            {
                if (_backgroundGeneration != null)
                {
                    Console.WriteLine("Switching background generation to foreground...");
                    backgroundOutputHandler.LogToFile = false; // Switch the background thread to start writing to the console since we're locking it up
                    await _backgroundGeneration;
                    _backgroundGeneration = null;
                }
                else
                {
                    await GenerateNewDiscoveries(dbContext, new OutputHandler());
                }
            }
        }

        var firstPending = pending[0];
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(firstPending.Text);
        Console.ResetColor();

        // Move to past discoveries
        Debug.Assert(dbContext != null);
        dbContext.PastDiscoveries.Add(new PastDiscovery
        {
            Text = firstPending.Text,
            DiscoveredAt = DateTime.UtcNow
        });
        dbContext.PendingDiscoveries.Remove(firstPending);
        dbContext.SaveChanges();
        dbContext.ChangeTracker.Clear();
    }

    private static void PrintInferPrompt(DiscoveryContext dbContext)
    {
        var interests = dbContext.Interests.Select(i => i.Name).ToList();
        var dislikes = dbContext.Dislikes.Select(d => d.Name).ToList();
        var pastDiscoveries = dbContext.PastDiscoveries.Select(pd => pd.CompactedText ?? pd.Text)
            .Concat(dbContext.PendingDiscoveries.Select(pd => pd.Text)).ToArray();

        Console.WriteLine(_discoveryService.GetInferPrompt(interests, dislikes, pastDiscoveries));
    }

    private static async Task GenerateNewDiscoveries(DiscoveryContext dbContext, OutputHandler output)
    {
        async Task Generate()
        {
            try
            {
                await _generationLock.WaitAsync();
                dbContext ??= new DiscoveryContext(); //Cannot be initialized statically because it's ThreadStatic

                output.WriteLine("\nCompacting past facts as an optimization...", ConsoleColor.Gray);
                await CompactPastDiscoveries(dbContext, output); //Ensure CompactedText exists for as many of the past discoveries as possible to minimize context usage (guessing probably a 70%-80% reduction).

                var interests = dbContext.Interests.Select(i => i.Name).ToList();
                var dislikes = dbContext.Dislikes.Select(d => d.Name).ToList();
                var pastDiscoveries = dbContext.PastDiscoveries.Select(pd => pd.CompactedText ?? pd.Text)
                    .Concat(dbContext.PendingDiscoveries.Select(pd => pd.Text)).ToArray(); //Include full text of pending discoveries if generating ahead (there would be none if not generating ahead, so same code)

                output.WriteLine("\nGenerating new facts...");
                var newDiscoveries = await _discoveryService.InferAsync(interests, dislikes, pastDiscoveries, _temperature, output);
                var evaluated = new List<string>();
                if (newDiscoveries.Any())
                {
                    output.WriteLine("\nEvaluating generated facts...");
                    evaluated.AddRange(await _discoveryService.EvaluateAsync(dislikes, pastDiscoveries, newDiscoveries, output));
                }

                if (evaluated.Count > 0)
                {
                    foreach (var discovery in evaluated)
                    {
                        dbContext.PendingDiscoveries.Add(new PendingDiscovery
                        {
                            Text = discovery,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    dbContext.SaveChanges();
                    output.WriteLine($"\n{evaluated.Count} new discover{(evaluated.Count == 1 ? "y" : "ies")} generated and added to pending list.");
                    _temperature = 0f;
                }
                else
                {
                    _temperature += 0.3f; //Keep trying with higher temperature until we come up with *something* new, but note: higher temperature = more hallucinations.
                    output.WriteLine($"\nNo new discoveries generated. Raising temperature to {_temperature}.", ConsoleColor.Yellow);
                    //For future consideration: we could instead seed it with some random words. I feel like that'd reduce the hallucination chance while increasing the topic randomness.
                }
            }
            finally
            {
                _generationLock.Release();
            }
        }

        if (output.LogToFile)
        {
            var tcs = new TaskCompletionSource();
            dbContext = new DiscoveryContext(); //Cannot be shared between threads
            var thread = new Thread(() =>
            {
                try
                {
                    Generate().GetAwaiter().GetResult();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            thread.Start();
            _backgroundGeneration = tcs.Task;
        }
        else
        {
            await Generate();
        }
    }

    private static async Task ReEvaluatePendingDiscoveries(DiscoveryContext dbContext)
    {
        var pending = dbContext.PendingDiscoveries.Select(pd => pd.Text).ToList();
        var dislikes = dbContext.Dislikes.Select(d => d.Name).ToList();
        var pastDiscoveries = dbContext.PastDiscoveries.Select(pd => pd.CompactedText ?? pd.Text).ToArray();

        var evaluated = await _discoveryService.EvaluateAsync(dislikes, pastDiscoveries, pending, new OutputHandler());

        //TODO: I wonder if we could also look for uncommon words in these and check if they appear in past discoveries and remove them if so, or perhaps an embedding model could do a better similarity check, or even modify the logits inside the sampling pipeline.

        // Remove all existing pending discoveries
        dbContext.PendingDiscoveries.RemoveRange(dbContext.PendingDiscoveries);
        dbContext.SaveChanges();

        // Add back the evaluated ones
        foreach (var discovery in evaluated)
        {
            dbContext.PendingDiscoveries.Add(new PendingDiscovery
            {
                Text = discovery,
                CreatedAt = DateTime.UtcNow
            });
        }
        dbContext.SaveChanges();
    }

    private static async Task CompactPastDiscoveries(DiscoveryContext dbContext, OutputHandler output)
    {
        var pastDiscoveries = dbContext.PastDiscoveries.Where(p => p.CompactedText == null).ToList();
        foreach (var group in pastDiscoveries.Chunk(20))
        {
            var lines = (await _discoveryService.CompactAsync([.. group.Select(p => p.Text)], output)).ToList();
            //Note: it's entirely possible that the compacting fails or misses some lines. LLMs just don't always answer well. Not much we can do about it.
            for (var i = 0; i < group.Length && i < lines.Count; i++)
            {
                group[i].CompactedText = lines[i];
            }
            dbContext.SaveChanges(); //Save after each chunk so you can stop the program and not lose that progress.
        }
    }
}
