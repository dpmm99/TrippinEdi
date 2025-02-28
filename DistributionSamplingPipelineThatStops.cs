﻿using LLama;
using LLama.Native;
using LLama.Sampling;

namespace TrippinEdi;

/// <summary>
/// They broke the InteractiveExecutor, so I have to identify the stop token and stop inference at that point myself
/// </summary>
internal class DistributionSamplingPipelineThatStops(LLamaWeights model, float temperature) : BaseSamplingPipeline
{
    private readonly StopTokenCatcher _stopTokenCatcher = new(model);

    public bool StopTokenReceived => _stopTokenCatcher.StopTokenReceived;

    private class StopTokenCatcher(LLamaWeights model) : ICustomSampler
    {
        public bool StopTokenReceived;

        public string Name => nameof(StopTokenCatcher);

        public void Accept(LLamaToken token)
        {
            StopTokenReceived = token == model.Vocab.EOS || token == model.Vocab.EOT; //Note: EOT is supposed to be used for the user's message, not the assistant's, but they often match, and it's surely possible for a model to infer it.
        }

        public void Apply(ref LLamaTokenDataArrayNative tokenData)
        {
        }

        public ICustomSampler Clone() => new StopTokenCatcher(model);

        public void Dispose()
        {
        }

        public void Reset()
        {
            StopTokenReceived = false;
        }
    }

    public Grammar? Grammar { get; init; }

    protected override SafeLLamaSamplerChainHandle CreateChain(SafeLLamaContextHandle context)
    {
        var chain = SafeLLamaSamplerChainHandle.Create(LLamaSamplerChainParams.Default());
        if (Grammar != null)
        {
            chain.AddGrammar(context.ModelHandle, Grammar.Gbnf, Grammar.Root);
        }

        if (temperature > 0)
        {
            chain.AddTopK(40);
            chain.AddTypical(1f, 1);
            chain.AddTopP(0.9f, 1);
            chain.AddMinP(0.1f, 1);

            chain.AddTemperature(temperature);
            chain.AddDistributionSampler((uint)Random.Shared.Next());
        }
        else
        {
            chain.AddGreedySampler();
        }

        chain.AddCustom(_stopTokenCatcher);
        return chain;
    }
}
