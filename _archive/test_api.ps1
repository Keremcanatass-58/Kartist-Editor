$ErrorActionPreference = "Stop"

try {
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $true
    $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
    
    $client = New-Object System.Net.Http.HttpClient($handler)
    
    $client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36")
    $client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8")
    $client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,tr;q=0.8")
    
    $url = "https://api.airforce/v1/imagine2?prompt=istanbul%20aesthetic%20night"
    Write-Host "Fetching $url"
    
    $response = $client.GetAsync($url).GetAwaiter().GetResult()
    Write-Host "StatusCode: $($response.StatusCode)"
    Write-Host "MediaType: $($response.Content.Headers.ContentType.MediaType)"
} catch {
    Write-Host "Error: $($_.Exception.Message)"
}
