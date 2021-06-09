﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Victoria.Enums;
using Victoria.Payloads.Player;
using Victoria.Wrappers;

namespace Victoria.Interfaces {
    /// <inheritdoc />
    public interface ILavaPlayer : ILavaPlayer<ILavaTrack> { }

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TLavaTrack"></typeparam>
    public interface ILavaPlayer<TLavaTrack> : IAsyncDisposable where TLavaTrack : ILavaTrack {
        /// <summary>
        /// 
        /// </summary>
        TLavaTrack Track { get; }

        /// <summary>
        /// 
        /// </summary>
        int Volume { get; }

        /// <summary>
        /// 
        /// </summary>
        DateTimeOffset LastUpdate { get; }

        /// <summary>
        /// 
        /// </summary>
        PlayerState PlayerState { get; }

        /// <summary>
        /// 
        /// </summary>
        ulong VoiceChannelId { get; }

        /// <summary>
        /// 
        /// </summary>
        IReadOnlyCollection<EqualizerBand> Bands { get; }

        /// <summary>
        /// 
        /// </summary>
        LavaQueue<TLavaTrack> Queue { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="lavaTrack"></param>
        /// <param name="noReplace"></param>
        /// <param name="volume"></param>
        /// <param name="shouldPause"></param>
        /// <exception cref="ArgumentNullException">Throws when <paramref name="lavaTrack"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Throws when <paramref name="volume"/> is less than 0.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Throws when <paramref name="volume"/> is greater than 1000.</exception>
        ValueTask PlayAsync(TLavaTrack lavaTrack, bool noReplace = true, int volume = default,
                            bool shouldPause = false);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="lavaTrack"></param>
        /// <param name="startTime"></param>
        /// <param name="stopTime"></param>
        /// <param name="noReplace"></param>
        /// <param name="volume"></param>
        /// <param name="shouldPause"></param>
        /// <exception cref="ArgumentNullException">Throws when <paramref name="lavaTrack"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Throws when <paramref name="startTime"/> is less than <paramref name="lavaTrack"/> start time.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Throws when <paramref name="stopTime"/> is greater than <paramref name="lavaTrack"/> end time.</exception>
        /// <exception cref="InvalidOperationException">Throws when star time is bigger than end time.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Throws when <paramref name="volume"/> is less than 0.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Throws when <paramref name="volume"/> is greater than 1000.</exception>
        ValueTask PlayAsync(TLavaTrack lavaTrack, TimeSpan startTime, TimeSpan stopTime, bool noReplace = true,
                            int volume = default, bool shouldPause = false);

        /// <summary>
        /// 
        /// </summary>
        ValueTask StopAsync();

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="InvalidOperationException">Throws when <see cref="PlayerState"/> is invalid.</exception>
        ValueTask PauseAsync();

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="InvalidOperationException">Throws when <see cref="PlayerState"/> is invalid.</exception>
        ValueTask ResumeAsync();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="skipAfter"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">Throws when <see cref="PlayerState"/> is invalid.</exception>
        ValueTask<(TLavaTrack Skipped, TLavaTrack Current)> SkipAsync(TimeSpan? skipAfter = default);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="seekPosition"></param>
        /// <exception cref="InvalidOperationException">Throws when <see cref="PlayerState"/> is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Throws when <paramref name="seekPosition"/> is greater than <see cref="CurrentTrack"/> length.</exception>
        ValueTask SeekAsync(TimeSpan seekPosition);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="volume"></param>
        /// <exception cref="ArgumentOutOfRangeException">Throws when <paramref name="volume"/> is less than 0.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Throws when <paramref name="volume"/> is greater than 1000.</exception>
        ValueTask SetVolumeAsync(int volume);

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="InvalidOperationException">Throws when <see cref="PlayerState"/> is invalid.</exception>
        ValueTask EqualizeAsync(params EqualizerBand[] equalizerBands);
    }
}