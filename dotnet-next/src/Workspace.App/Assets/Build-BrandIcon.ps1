# Rebuild only branding assets from the approved transparent imagegen source.
# No external dependencies, network, application launch, or shared project edits.
[CmdletBinding()]
param([string]$SourcePath = (Join-Path $PSScriptRoot 'deojon-pdf-workspace.source.png'), [string]$PreviewDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$sizes=@(16,20,24,32,48,64,128,256)
$source=[System.Drawing.Bitmap]::new($SourcePath)
$frames=[System.Collections.Generic.List[System.Drawing.Bitmap]]::new()
$payloads=[System.Collections.Generic.List[byte[]]]::new()
$report=[System.Collections.Generic.List[object]]::new()
try {
    # Fit the visible silhouette: ignore source alpha noise below 8/255 only for
    # bounds detection. Keep original pixels/alpha unchanged inside padded crop.
    $rect=[System.Drawing.Rectangle]::new(0,0,$source.Width,$source.Height)
    $locked=$source.LockBits($rect,[System.Drawing.Imaging.ImageLockMode]::ReadOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bytes=[byte[]]::new($locked.Stride*$source.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($locked.Scan0,$bytes,0,$bytes.Length)
        $minX=$source.Width; $minY=$source.Height; $maxX=-1; $maxY=-1
        for($y=0;$y -lt $source.Height;$y++) { for($x=0;$x -lt $source.Width;$x++) {
            if($bytes[$y*$locked.Stride+$x*4+3] -ge 8) {
                if($x -lt $minX){$minX=$x}; if($x -gt $maxX){$maxX=$x}
                if($y -lt $minY){$minY=$y}; if($y -gt $maxY){$maxY=$y}
            }
        }}
    } finally {$source.UnlockBits($locked)}
    if($maxX -lt 0){throw 'Source has no visible pixels.'}
    $minX=[Math]::Max(0,$minX-3);$minY=[Math]::Max(0,$minY-3)
    $maxX=[Math]::Min($source.Width-1,$maxX+3);$maxY=[Math]::Min($source.Height-1,$maxY+3)
    $crop=[System.Drawing.RectangleF]::new($minX,$minY,$maxX-$minX+1,$maxY-$minY+1)
    foreach($size in $sizes) {
        $frame=[System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $frames.Add($frame)
        $g=[System.Drawing.Graphics]::FromImage($frame)
        try {
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.CompositingMode=[System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $g.CompositingQuality=[System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $pad=[Math]::Max(1.0,$size/64.0)
            $scale=($size-2*$pad)/[Math]::Max($crop.Width,$crop.Height)
            $w=$crop.Width*$scale; $h=$crop.Height*$scale
            $dest=[System.Drawing.RectangleF]::new(($size-$w)/2,($size-$h)/2,$w,$h)
            $g.DrawImage($source,$dest,$crop,[System.Drawing.GraphicsUnit]::Pixel)
        } finally {$g.Dispose()}
        $transparent=0; $partial=0; $opaque=0
        for($y=0;$y -lt $size;$y++){for($x=0;$x -lt $size;$x++){
            $a=$frame.GetPixel($x,$y).A
            if($a -eq 0){$transparent++}elseif($a -eq 255){$opaque++}else{$partial++}
        }}
        if($transparent -eq 0 -or ($partial+$opaque) -eq 0){throw "Invalid alpha in $size frame."}
        $ms=[System.IO.MemoryStream]::new()
        try {
            if($size -eq 256){$frame.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png)}
            else {
                $bw=[System.IO.BinaryWriter]::new($ms,[System.Text.Encoding]::UTF8,$true)
                try {
                    $maskStride=[int]([Math]::Ceiling($size/32.0)*4)
                    # BITMAPINFOHEADER, bottom-up 32bpp BGRA XOR + 1bpp AND mask.
                    $bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size*2))
                    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
                    $bw.Write([uint32]($size*$size*4+$maskStride*$size))
                    1..4 | ForEach-Object {$bw.Write([uint32]0)}
                    for($y=$size-1;$y -ge 0;$y--){for($x=0;$x -lt $size;$x++){
                        $c=$frame.GetPixel($x,$y)
                        $bw.Write([byte]$c.B);$bw.Write([byte]$c.G);$bw.Write([byte]$c.R);$bw.Write([byte]$c.A)
                    }}
                    for($y=$size-1;$y -ge 0;$y--){
                        $row=[byte[]]::new($maskStride)
                        for($x=0;$x -lt $size;$x++){if($frame.GetPixel($x,$y).A -eq 0){
                            $idx=[int][Math]::Floor($x/8);$row[$idx]=$row[$idx] -bor (128 -shr ($x%8))
                        }}
                        $bw.Write($row)
                    }
                }finally{$bw.Dispose()}
            }
            $payloads.Add($ms.ToArray())
        } finally {$ms.Dispose()}
        $report.Add([pscustomobject]@{Size=$size;Bits=32;Encoding=$(if($size -eq 256){'PNG'}else{'DIB+AND'});Transparent=$transparent;PartialAlpha=$partial;Opaque=$opaque;Bytes=$payloads[$payloads.Count-1].Length})
    }
    $icoPath=Join-Path $PSScriptRoot 'deojon-pdf-workspace.ico'
    $out=[System.IO.File]::Create($icoPath);$writer=[System.IO.BinaryWriter]::new($out)
    try {
        $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
        $offset=6+16*$sizes.Count
        for($i=0;$i -lt $sizes.Count;$i++){
            $dimension=[byte]($sizes[$i]%256)
            $writer.Write($dimension);$writer.Write($dimension);$writer.Write([byte]0);$writer.Write([byte]0)
            $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$payloads[$i].Length);$writer.Write([uint32]$offset)
            $offset+=$payloads[$i].Length
        }
        foreach($payload in $payloads){$writer.Write($payload)}
    }finally{$writer.Dispose()}
    $frames[$frames.Count-1].Save((Join-Path $PSScriptRoot 'deojon-pdf-workspace.png'),[System.Drawing.Imaging.ImageFormat]::Png)
    # Parse the emitted directory independently, check all spans and decoder sizes.
    $ico=[System.IO.File]::ReadAllBytes($icoPath)
    if([BitConverter]::ToUInt16($ico,0) -ne 0 -or [BitConverter]::ToUInt16($ico,2) -ne 1 -or [BitConverter]::ToUInt16($ico,4) -ne 8){throw 'Bad ICONDIR.'}
    for($i=0;$i -lt $sizes.Count;$i++){
        $entry=6+16*$i;$length=[BitConverter]::ToUInt32($ico,$entry+8);$offset=[BitConverter]::ToUInt32($ico,$entry+12)
        if($offset+$length -gt $ico.Length -or $length -ne $payloads[$i].Length){throw 'Bad icon span.'}
        if($sizes[$i] -eq 256){
            # GDI+ Icon selection may return a smaller DIB for PNG-compressed 256px.
            $pngStream=[System.IO.MemoryStream]::new($ico,$offset,$length)
            $decoded=[System.Drawing.Image]::FromStream($pngStream)
            try{if($decoded.Width -ne 256 -or $decoded.Height -ne 256){throw 'PNG frame size mismatch.'}}finally{$decoded.Dispose();$pngStream.Dispose()}
        } else {
            $icon=[System.Drawing.Icon]::new($icoPath,$sizes[$i],$sizes[$i])
            try{if($icon.Width -ne $sizes[$i] -or $icon.Height -ne $sizes[$i]){throw "Decoder size mismatch $($sizes[$i])."}}finally{$icon.Dispose()}
        }
    }
    if($PreviewDirectory){
        [System.IO.Directory]::CreateDirectory($PreviewDirectory)|Out-Null
        foreach($i in 0,3){$frames[$i].Save((Join-Path $PreviewDirectory "icon-$($sizes[$i]).png"),[System.Drawing.Imaging.ImageFormat]::Png)}
        $preview=[System.Drawing.Bitmap]::new(800,390)
        $g=[System.Drawing.Graphics]::FromImage($preview)
        $font=[System.Drawing.Font]::new('Segoe UI',12)
        try{
            $g.Clear([System.Drawing.Color]::FromArgb(242,244,247))
            $g.DrawString('Source-preserving icon | native pixels and enlarged pixels',$font,[System.Drawing.Brushes]::Black,12,10)
            $g.DrawImageUnscaled($frames[0],20,55);$g.DrawImageUnscaled($frames[3],75,48)
            $g.DrawString('16px    32px',$font,[System.Drawing.Brushes]::Black,12,90)
            $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $g.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::Half
            $g.DrawImage($frames[0],[System.Drawing.Rectangle]::new(20,130,160,160))
            $g.DrawImage($frames[3],[System.Drawing.Rectangle]::new(220,130,160,160))
            $g.FillRectangle([System.Drawing.Brushes]::White,420,70,180,220)
            $g.FillRectangle([System.Drawing.Brushes]::DarkSlateGray,610,70,180,220)
            $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.DrawImage($frames[7],[System.Drawing.Rectangle]::new(430,90,160,160))
            $g.DrawImage($frames[7],[System.Drawing.Rectangle]::new(620,90,160,160))
            $g.DrawString('16px x10                 32px x5                 light / dark alpha',$font,[System.Drawing.Brushes]::Black,12,320)
            $g.DrawString('No recolor, redraw, letter resize, or silhouette modification.',$font,[System.Drawing.Brushes]::Black,12,350)
            $preview.Save((Join-Path $PreviewDirectory 'brand-icon-preview.png'),[System.Drawing.Imaging.ImageFormat]::Png)
        }finally{$font.Dispose();$g.Dispose();$preview.Dispose()}
        $report|ConvertTo-Json|Set-Content (Join-Path $PreviewDirectory 'brand-icon-metadata.json') -Encoding utf8
    }
    "Source: $($source.Width)x$($source.Height), nontransparent bounds: $crop"
    $report|Format-Table -AutoSize
    Get-FileHash $icoPath -Algorithm SHA256
}finally{foreach($frame in $frames){$frame.Dispose()};$source.Dispose()}
