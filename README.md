# Trippin' Edi

A local LLM-powered exploration assistant that helps you discover interesting facts and concepts within your areas of interest. The name comes from a retired service that allowed users to stumble upon interesting websites + edification.

## Overview

Trippin' Edi uses a large language model running on your local machine to generate and curate (hopefully) interesting facts tailored to your interests while avoiding topics you dislike. The facts are stored in a SQLite® database for persistence between sessions, allowing you to focus on one fact at a time without losing your place.

## Example Output

Given interests like these:
- programming (user knows C#, C, Arduino, Nintendo DS, optimization)
- game design

Trippin' Edi produced these facts:
- Use behavior trees in game AI for dynamic decision-making, offering a more efficient alternative to finite state machines.
- Enhance pathfinding efficiency in games using A* with jump point optimization for quicker route calculations.
- Utilize Thumb mode and DMA for faster data transfers, reducing CPU load and enhancing performance on the Nintendo DS.
- Employ multi-octave Perlin noise for detailed terrain generation, enhancing realism in procedural content.
- Implement a custom memory allocator for optimized memory usage in game development.
- Use Voronoi diagrams to create intricate dungeon layouts or creature patterns in game design
- Error correction codes like Hamming codes can detect and correct single-bit errors in embedded systems with minimal overhead.
- The x86 POPCNT instruction can count set bits 4-10 times faster than iterative bit counting methods.
- The MQTT-SN protocol reduces networking overhead by up to 50% compared to standard MQTT in bandwidth-constrained games.
- Quadtree spatial partitioning can reduce physics collision checks from O(n²) to O(n log n) in 2D games.

## Features

- Uses local LLMs via LLamaSharp--no API keys or internet connection required after setup, and no data leaves your machine
- Persistent storage of interests, dislikes, 'pending' facts, and 'discovered' facts--you can continue where you left off after closing the program
- Background generation of new facts while you research previous ones (note, however, this does not run the fact-compression step)
- The same LLM is used in a second stage to evaluate the facts, in an attempt to avoid duplicates and irrelevant content
- To reduce the loss of intelligence caused by long contexts, the same LLM is used to compress facts to 2-5 words
- In case the local LLM gets stuck, the program can generate prompts for use with other LLMs

## Limitations

- As with any LLM, the quality of the facts can vary widely.
  - Always search the Web to verify the information.
- Even the best 32B model I tried gets stuck in loops, spitting out past facts, as it goes from ~150 to ~200 facts generated.
  - To work around this, you can delete or move the database, re-open the program, and enter different interests and dislikes, e.g., more specifc ones.
- There's a high likelihood of getting introductory/conclusion sentences, as the model is not trained to generate standalone facts. Leaving these in the database can lead to worse results.
  - These can be removed manually via any tool that allows you to modify a SQLite database, such as [SQLiteStudio](https://sqlitestudio.pl/).

## Requirements

### Hardware
- Combined RAM + VRAM must be:
  - At least 16 GB for 14B parameter models in Q4_K_M quantization
  - At least 32 GB for 32B parameter models in Q4_K_M quantization
  - Less for smaller models or higher quantization levels, but models below 24B seem to be very bad at following the complex instructions
  - The higher the memory bandwidth, the faster the model will run
  - Generally, models that are bigger, newer, and less quantized (at least up to Q6_K or so) lead to better results

### Software
- .NET 8.0 or higher
- A GGUF format language model
- Should work on various operating systems, but has only been tested on Windows® 10

## Setup

1. Place a GGUF model file in the application directory
   - The program will use the largest GGUF file found
   - Recommended: use a hardlink (`mklink /H [filename] [original-path]` on Windows) to supply the model
   - Default fallback (and the most successful [model](https://huggingface.co/bartowski/FuseO1-DeepSeekR1-QwQ-SkyT1-Flash-32B-Preview-GGUF) I tried in this app): `C:\AI\FuseO1-DeekSeekR1-QwQ-SkyT1-32B-Preview-Q4_K_M.gguf`

2. Run the application
   - SQLite database is created automatically
   - No additional configuration needed

## Usage

1. Select option 1 to add interests
2. Select option 2 to add dislikes
3. Select option 3 to get a new fact; this will return the next fact or, if none are in `PendingDiscoveries`, immediately start to generate a batch (~30) of new facts
4. Select option 4 for background generation; this generates one batch of new facts and then stops
5. Select option 5 to get the prompt for use with other LLMs

## Technical Details

### LLamaSharp Backend
The application uses [LLamaSharp](https://github.com/SciSharp/LLamaSharp) as a wrapper for [llama.cpp](https://github.com/ggerganov/llama.cpp). It defaults to NVIDIA® CUDA® 12 inference if supported on your hardware. To use different backend packages:
1. Install the desired LLamaSharp.Backend.* NuGet package
2. You can either uninstall the other Backend packages or specify whether to enable/disable each backend via LLamaSharp's NativeLibraryConfig, e.g., `NativeLibraryConfig.All.WithCuda(enable: false).WithVulkan(enable: true)`
3. Modify `DiscoveryService.cs` model parameters as needed

### Database
- Uses SQLite with Entity Framework Core
- Database file: `discoveries.db` in application directory
- Tables:
  - Interests
  - Dislikes
  - PastDiscoveries
  - PendingDiscoveries

### Logs
- LLama.cpp logs: `llamacpp_log.txt`
- Background inference logs: `background_inference.txt`

### Model Configuration
Default settings in `DiscoveryService.cs`:
- Context size: 8192 tokens
- GPU layers: 99 (auto-reduces to 25 for models >20GB)
- Flash attention: enabled
- K/V cache: Q8_0 quantization
- Batch size: 2048 tokens

## License

This work is dedicated to the public domain.

This readme was mostly generated by Anthropic's Claude 3.5 Sonnet, as was roughly a third of the code.

We are not affiliated with any of the companies mentioned in this project. All trademarks and registered trademarks are the property of their respective owners.
Microsoft, Windows, and .NET are registered trademarks of Microsoft Corporation.
SQLite is a registered trademark of Hipp, Wyrick & Company, Inc.
NVIDIA and CUDA are registered trademarks of NVIDIA Corporation.
