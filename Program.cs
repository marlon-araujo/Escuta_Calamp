using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Projeto_Classes.Classes;
using Newtonsoft.Json.Linq;
using Projeto_Classes.Classes.Gerencial;
using System.Globalization;
using System.Data;

namespace Monitoramento_Calamp
{
    class Program
    {
        private static SortedDictionary<string, EndPoint> endPointsRecebidas;

        private static void Main(string[] args)
        {
            UdpClient server = new UdpClient(20500);
            var remoteEP = new IPEndPoint(IPAddress.Any, 20500);
            Console.WriteLine("Conectado !");

            while (true)
            {
                try
                {
                    //recebendo os bytes enviados pelo rastreador calamp
                    byte[] bytes = server.Receive(ref remoteEP);
                    Thread thread = new Thread(x => InterpretarThread(remoteEP, bytes, server));
                    thread.Start();
                }
                catch (Exception e)
                {
                    Console.WriteLine("\n" + e.ToString());
                }
            }

        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length).Where(x => x % 2 == 0).Select(x => Convert.ToByte(hex.Substring(x, 2), 16)).ToArray();
        }

        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        private static void InterpretarThread(IPEndPoint remoteEP, Byte[] bytes, UdpClient server)
        {
            string mensagem_recebida = ByteArrayToString(bytes);
            Byte[] bytes_send = StringToByteArray(string.Format("8305{0}01010201{1}000000000000", mensagem_recebida.Substring(4, 10), mensagem_recebida.Substring(22, 4)));
            server.Send(bytes_send, bytes_send.Length, remoteEP);
            Interpretar(mensagem_recebida);
        }

        private static void Interpretar(string mensagem)
        {          
            //evt msg
            string id = "";
            try
            {
                if (mensagem.Substring(20, 2) == "02")
                {
                    bool gravar = false;
                    Rastreador r = new Rastreador();
                    r.PorId(mensagem.Substring(4, 10));

                    Byte[] partes = StringToByteArray(mensagem.Substring(42, 8));
                    //int decimal_comparacao = 0x7fffffff;
                    DateTime data_GPS = new DateTime(1970, 1, 1).AddSeconds(uint.Parse(mensagem.Substring(26, 8), System.Globalization.NumberStyles.HexNumber));

                    if (data_GPS > DateTime.Now.AddYears(-10))
                    {
                        //DateTime DataRecebida = new DateTime();

                        #region Traduzindo a Mensagem do Rastreador
                        string latitude = (int.Parse(mensagem.Substring(42, 8), System.Globalization.NumberStyles.HexNumber) * 0.0000001).ToString().Replace(',', '.');
                        string longitude = (int.Parse(mensagem.Substring(50, 8), System.Globalization.NumberStyles.HexNumber) * 0.0000001).ToString().Replace(',', '.');
                        string velocidade = (uint.Parse(mensagem.Substring(66, 8), System.Globalization.NumberStyles.HexNumber) * 0.036).ToString().Replace(',', '.');
                        var atuadores = Convert.ToString(Convert.ToByte(mensagem.Substring(94, 2), 16), 2).PadLeft(8, '0');
                        var saidas = Convert.ToString(Convert.ToByte(mensagem.Substring(210, 8), 16), 2).PadLeft(32, '0');
                        string entradas = "" + atuadores[7] + (atuadores[6] == '1' ? '0' : '1') + (atuadores[5] == '1' ? '0' : '1') + saidas[18] + saidas[17] + saidas[16];
                        string hodometro = uint.Parse(mensagem.Substring(130, 8), System.Globalization.NumberStyles.HexNumber).ToString();
                        string horimetro = Convert.ToDecimal(uint.Parse(mensagem.Substring(202, 8), System.Globalization.NumberStyles.HexNumber) / 60.0).ToString("#0");
                        string power_voltage = (uint.Parse(mensagem.Substring(106, 8), System.Globalization.NumberStyles.HexNumber) / 1000.0).ToString().Replace(',', '.');
                        string bkp_voltage = "0";
                        #endregion

                        #region Preenchendo Objeto
                        Mensagens m = new Mensagens();
                        m.Codigo = 0;
                        m.Data_Gps = data_GPS.ToString("yyyy/MM/dd HH:mm:ss");
                        m.Data_Recebida = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                        m.Latitude = latitude;
                        m.Longitude = longitude;
                        id = m.ID_Rastreador = mensagem.Substring(4, 10);
                        m.Ras_codigo = r.Codigo;
                        m.Velocidade = Convert.ToDecimal(velocidade.Replace('.', ',')).ToString("#0", CultureInfo.InvariantCulture).Replace('.', ',');
                        m.Vei_codigo = (r.Vei_codigo != 0 ? r.Vei_codigo : 0);
                        m.Tipo_Mensagem = "STT";
                        m.Horimetro = Convert.ToInt32(horimetro);
                        m.Hodometro = hodometro;
                        m.Tensao = power_voltage;
                        m.Tipo_Alerta = "";
                        m.CodAlerta = 0;
                        m.Ignicao = entradas[0] == '1' ? true : false;
                        m.Bloqueio = entradas[4] == '1' ? true : false;
                        m.Sirene = entradas[5] == '1' ? true : false;
                        
                        m.Mensagem = "CLP400;" + mensagem.Substring(4, 10) + ";02;393;" + data_GPS.ToShortDateString() + ";" +
                                     data_GPS.ToShortTimeString() + ";0da1;" + latitude + ";" + longitude + ";" + velocidade +
                                     ";0;0;0;" + hodometro + ";" + power_voltage + ";" + entradas + ";" +
                                     (atuadores[7] == '1' ? "2" : "1") + ";" + mensagem.Substring(22, 4) + ";" + horimetro +
                                     ";" +
                                     bkp_voltage + ";0";

                        //m.Endereco = Mensagens.RequisitarEndereco(latitude, longitude);
                        m.Endereco = Util.BuscarEndereco(latitude, longitude);
                        #endregion

                        Console.WriteLine("\n" + m.Mensagem);

                        #region Tipo Alerta
                        if (uint.Parse(mensagem.Substring(100, 2), System.Globalization.NumberStyles.HexNumber) == 30 ||
                            uint.Parse(mensagem.Substring(100, 2), System.Globalization.NumberStyles.HexNumber) == 31 ||
                            uint.Parse(mensagem.Substring(100, 2), System.Globalization.NumberStyles.HexNumber) == 32)
                        {
                            m.Tipo_Mensagem = "STT";
                            m.Tipo_Alerta = "";
                            gravar = true;
                        }
                        else
                        {
                            #region Tipo Alerta Não STT

                            if (uint.Parse(mensagem.Substring(100, 2), System.Globalization.NumberStyles.HexNumber) == 61)
                            {
                                m.Tipo_Alerta = "Botão de Pânico Acionado";
                                m.CodAlerta = 3;
                                m.Tipo_Mensagem = "EMG";
                                gravar = true;
                            }
                            if (uint.Parse(mensagem.Substring(100, 2), System.Globalization.NumberStyles.HexNumber) == 62)
                            //entrada 2 desligada
                            {
                                m.Tipo_Alerta = "Sensor Auxiliar Desligado";
                                m.CodAlerta = 4;
                                m.Tipo_Mensagem = "EMG";
                                gravar = true;
                            }
                            else if (uint.Parse(mensagem.Substring(100, 2), System.Globalization.NumberStyles.HexNumber) == 63) //entrada 2 ligada
                            {
                                m.Tipo_Alerta = "Sensor Auxiliar Ligado";
                                m.CodAlerta = 5;
                                m.Tipo_Mensagem = "EMG";
                                gravar = true;
                            }
                            else if (uint.Parse(mensagem.Substring(100, 2), System.Globalization.NumberStyles.HexNumber) == 68)
                            {
                                m.Tipo_Alerta = "Energia Principal Removida";
                                m.CodAlerta = 2;
                                m.Tipo_Mensagem = "EMG";
                                gravar = true;
                            }
                            else if (uint.Parse(mensagem.Substring(100, 2), System.Globalization.NumberStyles.HexNumber) == 69)
                            {
                                m.Tipo_Alerta = "Energia Principal Ligada";
                                m.CodAlerta = 17;
                                m.Tipo_Mensagem = "EMG";
                                gravar = true;
                            }
                            else if (uint.Parse(mensagem.Substring(100, 2), System.Globalization.NumberStyles.HexNumber) == 70)
                            {
                                m.Tipo_Alerta = "Jammer Detectado";
                                m.CodAlerta = 12;
                                m.Tipo_Mensagem = "EMG";
                                gravar = true;
                            }
                            else if (uint.Parse(mensagem.Substring(100, 2), System.Globalization.NumberStyles.HexNumber) == 72)
                            {
                                m.Tipo_Alerta = "Bateria Interna Desligada";
                                m.CodAlerta = 19;
                                m.Tipo_Mensagem = "EMG";
                                gravar = true;
                            }
                            else if (uint.Parse(mensagem.Substring(100, 2), System.Globalization.NumberStyles.HexNumber) == 71)
                            {
                                m.Tipo_Alerta = "Bateria Interna Ligada";
                                m.CodAlerta = 18;
                                m.Tipo_Mensagem = "EMG";
                                gravar = true;
                            }

                            

                            #endregion
                        }
                        #endregion
                        
                        #region Gravar
                        if (gravar && m.Gravar())
                        {
                            gravar = false;
                            m.Tipo_Mensagem = "EMG";
                            if (r.veiculo != null)
                            {
                                m.Vei_codigo = r.Vei_codigo;

                                #region COMENTADO
                                /*
                                #region List<Cerca>
                                List<Cerca> areas = new Cerca().BuscarAreas("");
                                foreach (Cerca area in areas)
                                {
                                    //esta fora da area
                                    if (area.verifica_fora(m, area))
                                    {
                                        //TESTE PARA VERIFICAR SAIU DA CERCA, EVENTO TODA HORA
                                        try
                                        {
                                            if (r.veiculo.Cer_codigo != 0)
                                            {
                                                StreamWriter txtMensagemPerdida = new StreamWriter("TESTE_CERTA_DIF0.txt", true);
                                                txtMensagemPerdida.WriteLine("ENTROU COM != 0");
                                                txtMensagemPerdida.Close();
                                            }
                                            if (r.veiculo.Cer_codigo != null)
                                            {
                                                StreamWriter txtMensagemPerdida = new StreamWriter("TESTE_CERTA_DIFNULL.txt", true);
                                                txtMensagemPerdida.WriteLine("ENTROU COM != NULL");
                                                txtMensagemPerdida.Close();
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            StreamWriter txtMensagemPerdida = new StreamWriter("TESTE_CERTA_DIFNULL.txt", true);
                                            txtMensagemPerdida.WriteLine(e.Message);
                                            txtMensagemPerdida.Close();
                                        }
                                        

                                        if (r.veiculo.Cer_codigo != 0 && r.veiculo.Cer_codigo == area.Codigo)
                                        {
                                            //Remover da cerca, gravar evento que saiu da area de risco
                                            r.veiculo.Saiu(area.Codigo, area.Area_risco);
                                            m.Tipo_Alerta = "Saiu área de risco '" + area.Descricao + "'";
                                            m.CodAlerta = 16;
                                            gravar = true;
                                        }
                                    }
                                    else // esta dentro
                                    {
                                        if (r.veiculo.Cer_codigo == 0 || r.veiculo.Cer_codigo != area.Codigo)
                                        {
                                            //Insere a cerca no veiculo, garvar evento que entrou na area de risco
                                            r.veiculo.Entrou(area.Codigo, area.Area_risco);
                                            m.Tipo_Alerta = "Entrou área de risco '" + area.Descricao + "'";
                                            m.CodAlerta = 15;
                                            gravar = true;
                                        }
                                    }
                                    if (gravar)
                                    {
                                        m.Gravar();
                                        gravar = false;
                                    }
                                }
                                #endregion

                                #region List<Veiculo_Cerca>
                                List<Veiculo_Cerca> vcs = new Veiculo_Cerca().porVeiculo(r.veiculo.Codigo);
                                foreach (Veiculo_Cerca vc in vcs)
                                {
                                    //esta fora da area
                                    if (vc.cerca.verifica_fora(m, vc.cerca))
                                    {
                                        //mas estava dentro
                                        if (vc.dentro)
                                        {
                                            //trocar valor do vc para FORA, gravar evento que saiu na cerca em questao
                                            r.veiculo.Saiu(vc.cerca.Codigo, vc.cerca.Area_risco);
                                            m.Tipo_Alerta = "Saiu da Cerca '" + vc.cerca.Descricao + "'";
                                            m.CodAlerta = 14;
                                            gravar = true;
                                        }
                                    }
                                    else // esta dentro
                                    {
                                        if (!vc.dentro)
                                        {
                                            //trocar valor do vc para DENTRO, gravar evento que Entrou na cerca em questao
                                            r.veiculo.Entrou(vc.cerca.Codigo, vc.cerca.Area_risco);
                                            m.Tipo_Alerta = "Entrou na Cerca '" + vc.cerca.Descricao + "'";
                                            m.CodAlerta = 13;
                                            gravar = true;
                                        }
                                    }
                                    if (gravar)
                                    {
                                        m.Gravar();
                                        gravar = false;
                                    }
                                }
                                #endregion
                                */
                                

                                                                                                                              
                                //#region List<Cerca>
                                //List<Cerca> cercaLista = new Cerca().BuscarAreas("");

                                //foreach (Cerca cerca in cercaLista)
                                //{
                                //    //VERIFICA SE A CERCA ESTA REGISTRADA COMO AREA DE RISCO OU NÃO
                                //    if (cerca.Area_risco)
                                //    {
                                //        //SAI DA CERCA
                                //        if (cerca.verifica_fora(m, cerca))
                                //        {
                                //            if (r.veiculo.Cer_codigo != 0 && r.veiculo.Cer_codigo == cerca.Codigo && r.veiculo.Cer_Dentro == true)
                                //            {
                                //                //REMOVE VEICULO DA CERCA, GRAVA O EVENTO "SAIU DA AREA DE RISCO"
                                //                r.veiculo.SaiuEntrou(cerca.Codigo, true);
                                //                m.Tipo_Alerta = "Saiu área de risco '" + cerca.Descricao + "'";
                                //                m.CodAlerta = 16;
                                //                gravar = true;
                                //            }
                                //        }
                                //        //ENTRA NA CETRA
                                //        else
                                //        {
                                //            if (r.veiculo.Cer_codigo == 0 || r.veiculo.Cer_codigo != cerca.Codigo && r.veiculo.Cer_Dentro == false)
                                //            {
                                //                //REGISTRA O VEICULO NA CERCA, GRAVA EVENTO DE AREA DE RISCO
                                //                r.veiculo.SaiuEntrou(cerca.Codigo, false);
                                //                m.Tipo_Alerta = "Entrou área de risco '" + cerca.Descricao + "'";
                                //                m.CodAlerta = 15;
                                //                gravar = true;                                                
                                //            }
                                //        }
                                //        //SE GRAVAR FOR TRUE, ENTRA INSERE NAS MENSAGENS
                                //        if (gravar)
                                //        {
                                //            m.Gravar();
                                //            gravar = false;
                                //        }
                                //    }
                                //}
                                //#endregion
                                 
                                //#region List<Veiculo_Cerca>
                                //List<Veiculo_Cerca> veiculoCercaLista = new Veiculo_Cerca().porVeiculo(r.veiculo.Codigo);

                                //foreach (Veiculo_Cerca veiculoCerca in veiculoCercaLista)
                                //{
                                //    if (!veiculoCerca.cerca.Area_risco)

                                //    {
                                //        //FORA DA CERCA
                                //        if (veiculoCerca.cerca.verifica_fora(m, veiculoCerca.cerca))
                                //        {
                                //            if (veiculoCerca.Cer_codigo != 0 && veiculoCerca.dentro == true)
                                //            {
                                //                //ALTERA O VeiculoCera PARA DENTRO = 0, GERA EVENTO QUE SAIU DA CERCA
                                //                r.veiculo.SaiuEntrou(veiculoCerca.cerca.Codigo, true);
                                //                m.Tipo_Alerta = "Saiu da Cerca '" + veiculoCerca.cerca.Descricao + "'";
                                //                m.CodAlerta = 14;
                                //                gravar = true;
                                //            }
                                //        }
                                //        //DENTRO DA CERCA
                                //        else
                                //        {
                                //            if (veiculoCerca.Cer_codigo == 0 && veiculoCerca.dentro == false)
                                //            {
                                //                //ALTERA O VeiculoCera PARA DENTRO = 0, GERA EVENTO QUE SAIU DA CERCA
                                //                r.veiculo.SaiuEntrou(veiculoCerca.cerca.Codigo, false);
                                //                m.Tipo_Alerta = "Entrou na Cerca '" + veiculoCerca.cerca.Descricao + "'";
                                //                m.CodAlerta = 13;
                                //                gravar = true;
                                //            }
                                //        }
                                //        if (gravar)
                                //        {
                                //            m.Gravar();
                                //            gravar = false;
                                //        }
                                //    }
                                //}
                                //#endregion

                                #endregion

                                #region Area de Risco
                                var area_risco = Cerca.BuscarAreaRisco();
                                if (area_risco != null)
                                {
                                    foreach (DataRow item in area_risco.Rows)
                                    {
                                        //está dentro da area de risco -> ENTROU
                                        if (!Cerca.VerificaDentroCercaArea(Convert.ToInt32(item["Tipo_cerca"]), item["Posicoes"].ToString(), m.Latitude, m.Longitude))
                                        {
                                            //não estava na cerca
                                            if (!Cerca.VerificaDentroArea(Convert.ToInt32(item["Codigo"]), m.Vei_codigo))
                                            {
                                                //Console.WriteLine("-------> ENTROU");
                                                Cerca.IncluirExcluirVeiculoAreaRiscoCerca(true, true, m.Vei_codigo, Convert.ToInt32(item["Codigo"]));
                                                m.Tipo_Alerta = "Entrou área de risco '" + item["Descricao"] + "'";
                                                m.CodAlerta = 15;
                                                m.GravarEvento();
                                            }
                                        }
                                        //está fora da area de risco -> SAIU
                                        else
                                        {
                                            //não estava na cerca
                                            if (Cerca.VerificaDentroArea(Convert.ToInt32(item["Codigo"]), m.Vei_codigo))
                                            {
                                                //Console.WriteLine("-------> SAIU");
                                                Cerca.IncluirExcluirVeiculoAreaRiscoCerca(false, true, m.Vei_codigo, Convert.ToInt32(item["Codigo"]));
                                                m.Tipo_Alerta = "Saiu área de risco '" + item["Descricao"] + "'";
                                                m.CodAlerta = 16;
                                                m.GravarEvento();
                                            }
                                        }
                                    }
                                }
                                #endregion

                                #region Cercas
                                var cercas_veiculo = Cerca.BuscarCercas(m.Vei_codigo);
                                if (cercas_veiculo != null)
                                {
                                    if (cercas_veiculo.Rows.Count > 0)
                                    {
                                        foreach (DataRow item in cercas_veiculo.Rows)
                                        {
                                            //está dentro da cerca -> ENTROU
                                            if (!Cerca.VerificaDentroCercaArea(Convert.ToInt32(item["Tipo_cerca"]), item["Posicoes"].ToString(), m.Latitude, m.Longitude))
                                            {
                                                if (Convert.ToInt32(item["Dentro"]) == 0)
                                                {
                                                    //Console.WriteLine("-------> ENTROU");
                                                    Cerca.IncluirExcluirVeiculoAreaRiscoCerca(true, false, m.Vei_codigo, Convert.ToInt32(item["Codigo"]));
                                                    m.Tipo_Alerta = "Entrou cerca '" + item["Descricao"] + "'";
                                                    m.CodAlerta = 13;
                                                    m.GravarEvento();
                                                }
                                            }
                                            //está fora da cerca -> SAIU
                                            else
                                            {
                                                if (Convert.ToInt32(item["Dentro"]) == 1)
                                                {
                                                    //Console.WriteLine("-------> SAIU");
                                                    Cerca.IncluirExcluirVeiculoAreaRiscoCerca(false, false, m.Vei_codigo, Convert.ToInt32(item["Codigo"]));
                                                    m.Tipo_Alerta = "Saiu cerca '" + item["Descricao"] + "'";
                                                    m.CodAlerta = 14;
                                                    m.GravarEvento();
                                                }
                                            }
                                        }
                                    }
                                }
                                #endregion
                            }
                        }
                        #endregion

                        //Evento Por E-mail
                        Mensagens.EventoPorEmail(m.Vei_codigo, m.CodAlerta, m.Tipo_Alerta);
                    }
                }
            }
            catch (Exception e)
            {
                /*StreamWriter wr = new StreamWriter("Erro interpretacao.txt", true);
                wr.WriteLine(string.Format("ERRO:{0} /n DATA:{1} ID:{2} LOCAL:{3}", e.ToString(), DateTime.Now, id, e.StackTrace));
                wr.Close();*/
            }
        }

        private static string RequisitarEndereco(string lat, string lon)
        {
            try
            {
                const string end = "http://nominatim.openstreetmap.org/reverse?format=json&lat={0}&lon={1}&addressdetails=1";
                var wr = WebRequest.Create(string.Format(end, lat, lon));
                wr.ContentType = "application/json; charset=utf-8";
                dynamic json;
                StreamReader sr = new StreamReader(wr.GetResponse().GetResponseStream(), Encoding.UTF8);
                json = JObject.Parse(sr.ReadToEnd());
                if (json.address != null)
                {
                    return json.address.road + ", " + json.address.suburb + ", " + json.address.city + ", " +
                              json.address.state + ", " + json.address.country;
                }
                return "Endereço Indisponível";
            }
            catch (Exception e)
            {
                string end;
                try
                {
                    end = "http://maps.googleapis.com/maps/api/geocode/json?latlng={0},{1}";
                    var wr = WebRequest.Create(string.Format(end, lat, lon));
                    wr.ContentType = "application/json; charset=utf-8";
                    dynamic json;
                    StreamReader sr = new StreamReader(wr.GetResponse().GetResponseStream(), Encoding.UTF8);
                    json = JObject.Parse(sr.ReadToEnd());
                    if (json.results.Count != 0)
                    {
                        return json.results[0].formatted_address.ToString();
                    }
                    else
                    {
                        end = "http://reverse.geocoder.cit.api.here.com/6.2/reversegeocode.json?app_id=t4XCyrcwQI93nV2TdYlR&app_code=EraHXapXt_0aguDSKCrMJA&gen=8&prox={0},{1},100&mode=retrieveAddresses";
                        wr = WebRequest.Create(string.Format(end, lat, lon));
                        wr.ContentType = "application/json; charset=utf-8";

                        sr = new StreamReader(wr.GetResponse().GetResponseStream(), Encoding.UTF8);
                        json = JObject.Parse(sr.ReadToEnd());
                        if (json.Response.View.Count != 0)
                        {
                            return json.Response.View[0].Result[0].Location.Address.Label;
                        }
                    }
                    return "Endereço Indisponível";

                }
                catch (Exception ex)
                {
                    Console.WriteLine("\n" + ex.Message);
                    return "";
                }
            }
        }
    }
}
