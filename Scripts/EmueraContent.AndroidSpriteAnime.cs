using System;
using System.Collections.Generic;
using Godot;
using MinorShift.Emuera.Content;

public partial class EmueraContent
{
	const int AndroidSpriteAnimeFrameCacheTarget = 16;
	const long AndroidSpriteAnimeFrameBudgetBytes = 32L * 1024L * 1024L;
	const ulong AndroidSpriteAnimeFrameIdleMs = 30000;
	const ulong AndroidSpriteAnimeFrameCleanupIntervalMs = 3000;

	sealed class AndroidSpriteAnimeFrameTextureEntry : IDisposable
	{
		public SpriteManager.TextureInfo SourceInfo;
		public Image FrameImage;
		public ImageTexture Texture;
		public Rect2I SourceRegion;
		public ulong LastUsedMs;
		public long EstimatedBytes;

		public void Dispose()
		{
			Texture?.Dispose();
			Texture = null;
			FrameImage?.Dispose();
			FrameImage = null;
			SourceInfo = null;
		}
	}

	readonly Dictionary<SpriteAnime, AndroidSpriteAnimeFrameTextureEntry> androidSpriteAnimeFrameTextures = new();
	ulong lastAndroidSpriteAnimeFrameCleanupMs;

	static bool UseAndroidSpriteAnimeFrameTexture => OS.GetName() == "Android";

	// Android 设备不一定能可靠上传 8000px 宽的 AS06 图集，而且单张完整 RGBA 纹理会占用
	// 约 92~122 MiB 显存。这里保留 CPU 侧整图供裁剪，但 GPU 只持有当前 500x500/300x300
	// 小帧；同一 SpriteAnime 始终更新同一张 ImageTexture，避免逐帧创建纹理和无界缓存。
	Texture2D GetAndroidSpriteAnimeFrameTexture(SpriteAnime anime,
		uEmuera.Drawing.BitmapTexture bitmap, uEmuera.Drawing.Rectangle srcRect)
	{
		if (!UseAndroidSpriteAnimeFrameTexture || anime == null || bitmap == null)
			return null;

		var ti = bitmap.CachedTextureInfo;
		if (ti == null)
		{
			if (bitmap.RequestTextureInfoAsync())
				TrackAsyncTextureRequestForCurrentRender();
			return null;
		}
		if (ti.IsPlaceholder)
		{
			if (bitmap.RequestTextureInfoAsync())
				TrackAsyncTextureRequestForCurrentRender();
			return null;
		}

		Image sourceImage = ti.image;
		if (sourceImage == null || srcRect.Width <= 0 || srcRect.Height <= 0
			|| srcRect.X < 0 || srcRect.Y < 0
			|| srcRect.X + srcRect.Width > sourceImage.GetWidth()
			|| srcRect.Y + srcRect.Height > sourceImage.GetHeight())
		{
			return null;
		}

		TrackTexturePin(ti);
		var region = new Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
		Image.Format format = sourceImage.GetFormat();
		bool recreate = !androidSpriteAnimeFrameTextures.TryGetValue(anime, out var entry)
			|| entry == null
			|| !ReferenceEquals(entry.SourceInfo, ti)
			|| entry.FrameImage == null
			|| entry.Texture == null
			|| entry.FrameImage.GetWidth() != srcRect.Width
			|| entry.FrameImage.GetHeight() != srcRect.Height
			|| entry.FrameImage.GetFormat() != format;

		if (recreate)
		{
			entry?.Dispose();
			entry = new AndroidSpriteAnimeFrameTextureEntry
			{
				SourceInfo = ti,
				FrameImage = Image.CreateEmpty(srcRect.Width, srcRect.Height, false, format),
				SourceRegion = region,
				EstimatedBytes = (long)srcRect.Width * srcRect.Height * 4L,
			};
			entry.FrameImage.BlitRect(sourceImage, region, Vector2I.Zero);
			entry.Texture = ImageTexture.CreateFromImage(entry.FrameImage);
			androidSpriteAnimeFrameTextures[anime] = entry;
		}
		else if (entry.SourceRegion != region)
		{
			entry.FrameImage.BlitRect(sourceImage, region, Vector2I.Zero);
			entry.Texture.Update(entry.FrameImage);
			entry.SourceRegion = region;
		}

		entry.LastUsedMs = Time.GetTicksMsec();
		return entry.Texture;
	}

	// 仅在缓存超过常用规模后清理长时间未触达的动画。可见 Canvas 动画每 50ms 会触达，
	// 因此不会被回收；离屏历史行重新进入可见区时会按当前帧自动重建小纹理。
	void CleanupAndroidSpriteAnimeFrameTextures()
	{
		if (!UseAndroidSpriteAnimeFrameTexture || androidSpriteAnimeFrameTextures.Count == 0)
		{
			return;
		}
		long estimatedBytes = 0;
		foreach (var entry in androidSpriteAnimeFrameTextures.Values)
			estimatedBytes += entry?.EstimatedBytes ?? 0;
		if (androidSpriteAnimeFrameTextures.Count <= AndroidSpriteAnimeFrameCacheTarget
			&& estimatedBytes <= AndroidSpriteAnimeFrameBudgetBytes)
			return;

		ulong now = Time.GetTicksMsec();
		if (lastAndroidSpriteAnimeFrameCleanupMs != 0
			&& now - lastAndroidSpriteAnimeFrameCleanupMs < AndroidSpriteAnimeFrameCleanupIntervalMs)
		{
			return;
		}
		lastAndroidSpriteAnimeFrameCleanupMs = now;

		var removals = new List<SpriteAnime>();
		foreach (var item in androidSpriteAnimeFrameTextures)
		{
			if (now - item.Value.LastUsedMs > AndroidSpriteAnimeFrameIdleMs)
				removals.Add(item.Key);
		}
		for (int i = 0; i < removals.Count; i++)
		{
			if (androidSpriteAnimeFrameTextures.Remove(removals[i], out var entry))
				entry.Dispose();
		}
	}

	void DisposeAndroidSpriteAnimeFrameTextures()
	{
		foreach (var entry in androidSpriteAnimeFrameTextures.Values)
			entry?.Dispose();
		androidSpriteAnimeFrameTextures.Clear();
		lastAndroidSpriteAnimeFrameCleanupMs = 0;
	}
}
