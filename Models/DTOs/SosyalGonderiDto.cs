using System;

namespace Kartist.Models.DTOs
{
    public class SosyalGonderiDto
    {
        public int Id { get; set; }
        public string Icerik { get; set; }
        public string GorselUrl { get; set; }
        public int BegeniSayisi { get; set; }
        public int YorumSayisi { get; set; }
        public DateTime OlusturmaTarihi { get; set; }
        public string KodSinipet { get; set; }
        public string OnceSonraResim { get; set; }
        public string AiVibe { get; set; }
        
        public int KullaniciId { get; set; }
        public string AdSoyad { get; set; }
        public string ProfilResmi { get; set; }
        public string UyelikTipi { get; set; }
        
        // Contextual Fields (computed based on current user session)
        public int Begenildi { get; set; } // 1 or 0
        public int Kaydedildi { get; set; } // 1 or 0
        public int IsMyPost { get; set; } // 1 or 0
    }
}
