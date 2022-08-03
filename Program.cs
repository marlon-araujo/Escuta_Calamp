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
using System.Xml;
using System.Collections;

namespace Monitoramento_Calamp
{
    class Program
    {
        //private static SortedDictionary<string, EndPoint> endPointsRecebidas;
        private static ArrayList contas = new ArrayList();

        private static void Main(string[] args)
        {

            #region Contas HERE

            XmlDocument xDoc = new XmlDocument();
            xDoc.Load("END_POINT");

            XmlNodeList coluna = xDoc.GetElementsByTagName("coluna");
            XmlNodeList app_id = xDoc.GetElementsByTagName("app_id");
            XmlNodeList app_code = xDoc.GetElementsByTagName("app_code");
            XmlNodeList inicio = xDoc.GetElementsByTagName("inicio");
            XmlNodeList fim = xDoc.GetElementsByTagName("fim");

            for (int i = 0; i < coluna.Count; i++)
            {
                ArrayList itens = new ArrayList();
                itens.Add(coluna[i].InnerText);
                itens.Add(app_id[i].InnerText);
                itens.Add(app_code[i].InnerText);
                itens.Add(inicio[i].InnerText);
                itens.Add(fim[i].InnerText);
                contas.Add(itens);
            }

            #endregion


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
                catch (Exception ex)
                {
                    //LogException.GravarException("Erro: " + ex.Message.ToString() + " - Mensagem: " + (ex.InnerException != null ? ex.InnerException.ToString() : " Valor nulo na mensagem "), 12, "Escuta Calamp - Método " + System.Reflection.MethodBase.GetCurrentMethod().Name);
                    Console.WriteLine("\n" + ex.ToString());
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
                        m.Data_Rastreador = data_GPS.ToString("yyyy/MM/dd HH:mm:ss");
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
                        m.Endereco = Util.BuscarEndereco(latitude, longitude, contas);
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
                            m.Tipo_Mensagem = "EMG";
                            if (r.veiculo != null)
                            {
                                Mensagens.EventoAreaCerca(m);

                                //Evento Por E-mail
                                var corpoEmail = m.Tipo_Alerta + "<br /> Endereço: " + m.Endereco;
                                Mensagens.EventoPorEmail(m.Vei_codigo, m.CodAlerta, corpoEmail);
                            }

                            #region Velocidade
                            if (r.Vei_codigo != 0)
                            {
                                var veiculo = Veiculo.BuscarVeiculoVelocidade(m.Vei_codigo);
                                var velocidade_nova = Convert.ToDecimal(veiculo.vei_velocidade);
                                if (velocidade_nova < Convert.ToDecimal(m.Velocidade) && velocidade_nova > 0)
                                {
                                    m.Tipo_Mensagem = "EVT";
                                    m.Tipo_Alerta = "Veículo Ultrapassou a Velocidade";
                                    m.CodAlerta = 23;
                                    m.GravarEvento();

                                    //Evento Por E-mail
                                    var corpoEmail = m.Tipo_Alerta + "<br /> Velocidade: " + m.Velocidade + "<br /> Endereço: " + m.Endereco;
                                    Mensagens.EventoPorEmail(m.Vei_codigo, m.CodAlerta, corpoEmail);
                                }
                            }
                            #endregion

                        }
                        #endregion
                    }
                }
            }
            catch (Exception ex)
            {
                //LogException.GravarException("Erro: " + ex.Message.ToString() + " - Mensagem: " + (ex.InnerException != null ? ex.InnerException.ToString() : " Valor nulo na mensagem "), 12, "Escuta Calamp - Método " + System.Reflection.MethodBase.GetCurrentMethod().Name);
                /*StreamWriter wr = new StreamWriter("Erro interpretacao.txt", true);
                wr.WriteLine(string.Format("ERRO:{0} /n DATA:{1} ID:{2} LOCAL:{3}", e.ToString(), DateTime.Now, id, e.StackTrace));
                wr.Close();*/
            }
        }
    }
}
