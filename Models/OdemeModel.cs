namespace Kartist.Models
{
    public class OdemeModel
    {
        public int KartId { get; set; }
        public string KartSahibi { get; set; }
        public string KartNumarasi { get; set; }
        public string SonKullanmaAy { get; set; }
        public string SonKullanmaYil { get; set; }
        public string Cvc { get; set; }
        public decimal Fiyat { get; set; }
    }
}