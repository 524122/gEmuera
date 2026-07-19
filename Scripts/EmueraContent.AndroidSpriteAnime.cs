using System;
using System.Collections.Generic;
using Godot;
using MinorShift.Emuera.Content;

public partial class EmueraContent
{
	const int AndroidCroppedAtlasTextureCacheTarget = 16;
	const long AndroidCroppedAtlasTextureBudgetBytes = 32L * 1024L * 1024L;
	const ulong AndroidCroppedAtlasTextureIdleMs = 30000;
	const ulong AndroidCroppedAtlasTextureCleanupIntervalMs = 3000;
	const int AndroidFullAtlasTextureMaxSize = 4096;

	sealed class AndroidCroppedAtlasTextureEntry : IDisposable
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

	// key 同时接受 SpriteAnime 和静态 ASpriteSingle。两者都从同一份 CPU 图集中裁小块，
	// 从而不会把 8192px 宽的图集直接上传到 Android GPU。
	readonly Dictionary<object, AndroidCroppedAtlasTextureEntry> androidCroppedAtlasTextures = new();
	ulong lastAndroidCroppedAtlasTextureCleanupMs;

	static bool UseAndroidCroppedAtlasTexture => OS.GetName() == "Android";

	// Android 设备不一定能可靠上传 8000px 宽的图集，而且单张完整 RGBA 纹理会占用
	// 约 72 MiB 显存。这里保留 CPU 侧整图供裁剪，但 GPU 只持有实际显示的帧/图块；
	// 动画同一对象复用一张 ImageTexture，静态图块则按 sprite 缓存，避免逐帧创建纹理和无界缓存。
	Texture2D GetAndroidSpriteAnimeFrameTexture(SpriteAnime anime,
		uEmuera.Drawing.BitmapTexture bitmap, uEmuera.Drawing.Rectangle srcRect)
	{
		if (!UseAndroidCroppedAtlasTexture || anime == null || bitmap == null)
			return null;

		if (!TryGetAndroidAtlasSource(bitmap, out var ti, out var sourceImage))
		{
			return null;
		}
		return GetOrCreateAndroidCroppedAtlasTexture(anime, ti, sourceImage, srcRect);
	}

	// 静态 CSV sprite 的源矩形仍使用原始图集坐标。若先让 TextureInfo 把 8192px 图集
	// 缩成 4096px，再用原坐标构造 AtlasTexture，就会越界采样成横条。因此必须在访问
	// ti.texture 之前先裁出小图块。返回 true 表示调用方不能再回退到整图 AtlasTexture 路径。
	bool TryGetAndroidStaticAtlasRegionTexture(ASpriteSingle sprite,
		uEmuera.Drawing.BitmapTexture bitmap, uEmuera.Drawing.Rectangle srcRect, out Texture2D texture)
	{
		texture = null;
		if (!UseAndroidCroppedAtlasTexture || sprite == null || bitmap == null)
			return false;
		if (!TryGetAndroidAtlasSource(bitmap, out var ti, out var sourceImage))
			return false;

		int sourceWidth = sourceImage.GetWidth();
		int sourceHeight = sourceImage.GetHeight();
		if (sourceWidth <= AndroidFullAtlasTextureMaxSize && sourceHeight <= AndroidFullAtlasTextureMaxSize)
			return false;

		// 完整大图仍交给既有的缩放上传逻辑；这里只处理能安全变成小纹理的 Atlas 子区域。
		if (srcRect.X == 0 && srcRect.Y == 0
			&& srcRect.Width == sourceWidth && srcRect.Height == sourceHeight)
			return false;

		texture = GetOrCreateAndroidCroppedAtlasTexture(sprite, ti, sourceImage, srcRect);
		return true;
	}

	bool TryGetAndroidAtlasSource(uEmuera.Drawing.BitmapTexture bitmap,
		out SpriteManager.TextureInfo ti, out Image sourceImage)
	{
		ti = null;
		sourceImage = null;
		if (bitmap == null)
			return false;

		ti = bitmap.CachedTextureInfo;
		if (ti == null || ti.IsPlaceholder)
		{
			if (bitmap.RequestTextureInfoAsync())
				TrackAsyncTextureRequestForCurrentRender();
			return false;
		}

		sourceImage = ti.image;
		return sourceImage != null;
	}

	Texture2D GetOrCreateAndroidCroppedAtlasTexture(object cacheKey, SpriteManager.TextureInfo ti,
		Image sourceImage, uEmuera.Drawing.Rectangle srcRect)
	{
		if (cacheKey == null || ti == null || sourceImage == null || srcRect.Width <= 0 || srcRect.Height <= 0
			|| srcRect.X < 0 || srcRect.Y < 0
			|| srcRect.X + srcRect.Width > sourceImage.GetWidth()
			|| srcRect.Y + srcRect.Height > sourceImage.GetHeight())
		{
			if (GenericUtils.IsImageDebugEnabled("texture"))
			{
				GenericUtils.ImageTrace("IMAGE.ATLAS.CROP_FAIL", () => "atlas crop source rectangle is invalid",
					() => $"name={ti?.imagename ?? string.Empty} region={srcRect.X},{srcRect.Y},{srcRect.Width},{srcRect.Height} source={sourceImage?.GetWidth() ?? 0}x{sourceImage?.GetHeight() ?? 0} failure_kind=invalid_region");
			}
			return null;
		}

		TrackTexturePin(ti);
		var region = new Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
		Image.Format format = sourceImage.GetFormat();
		bool recreate = !androidCroppedAtlasTextures.TryGetValue(cacheKey, out var entry)
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
			entry = new AndroidCroppedAtlasTextureEntry
			{
				SourceInfo = ti,
				FrameImage = Image.CreateEmpty(srcRect.Width, srcRect.Height, false, format),
				SourceRegion = region,
				EstimatedBytes = (long)srcRect.Width * srcRect.Height * 4L,
			};
			try
			{
				entry.FrameImage.BlitRect(sourceImage, region, Vector2I.Zero);
				entry.Texture = ImageTexture.CreateFromImage(entry.FrameImage);
				androidCroppedAtlasTextures[cacheKey] = entry;
			}
			catch (Exception ex)
			{
				entry.Dispose();
				if (GenericUtils.IsImageDebugEnabled("texture"))
				{
					GenericUtils.ImageTrace("IMAGE.ATLAS.CROP_FAIL", () => "atlas crop texture creation failed",
						() => $"name={ti.imagename} region={srcRect.X},{srcRect.Y},{srcRect.Width},{srcRect.Height} failure_kind=texture_create_fail error={ex.GetType().Name}");
				}
				return null;
			}
		}
		else if (entry.SourceRegion != region)
		{
			try
			{
				entry.FrameImage.BlitRect(sourceImage, region, Vector2I.Zero);
				entry.Texture.Update(entry.FrameImage);
				entry.SourceRegion = region;
			}
			catch (Exception ex)
			{
				if (GenericUtils.IsImageDebugEnabled("texture"))
				{
					GenericUtils.ImageTrace("IMAGE.ATLAS.CROP_FAIL", () => "atlas crop texture update failed",
						() => $"name={ti.imagename} region={srcRect.X},{srcRect.Y},{srcRect.Width},{srcRect.Height} failure_kind=texture_update_fail error={ex.GetType().Name}");
				}
				return null;
			}
		}

		entry.LastUsedMs = Time.GetTicksMsec();
		return entry.Texture;
	}

	// 仅在缓存超过常用规模后清理长时间未触达的动画或静态图块。可见 Canvas 动画每 50ms
	// 会触达；离屏历史行重新进入可见区时会按当前帧或源矩形自动重建小纹理。
	void CleanupAndroidSpriteAnimeFrameTextures()
	{
		if (!UseAndroidCroppedAtlasTexture || androidCroppedAtlasTextures.Count == 0)
		{
			return;
		}
		long estimatedBytes = 0;
		foreach (var entry in androidCroppedAtlasTextures.Values)
			estimatedBytes += entry?.EstimatedBytes ?? 0;
		if (androidCroppedAtlasTextures.Count <= AndroidCroppedAtlasTextureCacheTarget
			&& estimatedBytes <= AndroidCroppedAtlasTextureBudgetBytes)
			return;

		ulong now = Time.GetTicksMsec();
		if (lastAndroidCroppedAtlasTextureCleanupMs != 0
			&& now - lastAndroidCroppedAtlasTextureCleanupMs < AndroidCroppedAtlasTextureCleanupIntervalMs)
		{
			return;
		}
		lastAndroidCroppedAtlasTextureCleanupMs = now;

		var removals = new List<object>();
		foreach (var item in androidCroppedAtlasTextures)
		{
			if (now - item.Value.LastUsedMs > AndroidCroppedAtlasTextureIdleMs)
				removals.Add(item.Key);
		}
		for (int i = 0; i < removals.Count; i++)
		{
			if (androidCroppedAtlasTextures.Remove(removals[i], out var entry))
				entry.Dispose();
		}
	}

	void DisposeAndroidSpriteAnimeFrameTextures()
	{
		foreach (var entry in androidCroppedAtlasTextures.Values)
			entry?.Dispose();
		androidCroppedAtlasTextures.Clear();
		lastAndroidCroppedAtlasTextureCleanupMs = 0;
	}
}
