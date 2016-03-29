using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Topshelf;
using JiraApiOpenSourseLibrary.JiraRestClient;

namespace DutyBot
{
    internal class Program
    {
        public static void Main()
        {
            //этот код создаёт и конфигурирует службу, используется Topshelf
            
            HostFactory.Run(x =>
            {
                x.Service<Prog>(s =>
                {
                    s.ConstructUsing(name => new Prog());
                    s.WhenStarted(tc => tc.Start());   
                    s.WhenStopped(tc => tc.Stop());
                });
                x.RunAsNetworkService();
                x.SetDescription("Service for JIRA. Adds lable watch when all linked issues closed");
                x.SetDisplayName("JiraWatcher");
                x.SetServiceName("JiraWatcher");
                x.StartAutomaticallyDelayed();
            });
        }
    }  

    internal class Prog
    {
        private Parametr jiraParam; //адрес jira с которой работаем
        private Parametr userLoginParam; //под кем будет ходить Бот при мониторинге жира во время дежурств (логин)
        private Parametr userPasswordParam; //под кем будет ходить Бот при мониторинге жира во время дежурств (пароль) 
        private Parametr filterParam; //какой фильтр мониторим
        
        public void Start() //метод вызывается при старте службы
        {
            try
            {
                try //пишем в лог о запуске службы
                {
                    using (var repository = new Repository<DbContext>())
                    {
                        var logReccord = new Log
                        {
                            Date = DateTime.Now,
                            MessageTipe = "info",
                            Operation = "StartService",
                            Exception = ""
                        };
                        repository.Create(logReccord);
                    }
                }
                catch (Exception ex)
                {
                    Thread.Sleep(60000); // еcли не доступна БД и не получается залогировать запуск, ждём 60 секунд и пробуем еще раз.
                    using (var repository = new Repository<DbContext>())
                    {
                        var exReccord = new Log
                        {
                            Date = DateTime.Now,
                            MessageTipe = "error",
                            Operation = "StartService",
                            Exception = ex.GetType() + ": " + ex.Message
                        };
                        repository.Create(exReccord);
                        var logReccord = new Log
                        {
                            Date = DateTime.Now,
                            MessageTipe = "info",
                            Operation = "StartService2Attemp",
                            Exception = ""
                        };
                        repository.Create(logReccord);
                    }

                }

                using (var repository = new Repository<DbContext>()) //инициализирую парамтры приложения
                {
                    jiraParam = repository.Get<Parametr>(p => p.Name == "jira");
                    userLoginParam = repository.Get<Parametr>(p => p.Name == "dafaultuserlogin");
                    userPasswordParam = repository.Get<Parametr>(p => p.Name == "dafaultuserpassword");
                    filterParam = repository.Get<Parametr>(p => p.Name == "Filter");
                }

                CheckJira(); //метод в бесконечном цикле будет проверять jira 

            }
            catch (Exception ex)
            {
                using (var repository = new Repository<DbContext>())
                {
                   repository.Create(new Log
                    {
                        Date = DateTime.Now,
                        MessageTipe = "fatal",
                        Operation = "StartService",
                        Exception = ex.GetType() + ": " + ex.Message
                    });
                    
                }
            }
        }
        public void Stop() //метод вызывается при остановке службы
        {
            try
            {
                using (var repository = new Repository<DbContext>())
                {
                    var logReccord = new Log
                    {
                        Date = DateTime.Now,
                        MessageTipe = "info",
                        Operation = "StopService",
                        Exception = "",
                    };
                    repository.Create(logReccord);
                }
                Thread.ResetAbort();
            }
            catch (Exception ex)
            {
                using (var repository = new Repository<DbContext>())
                {
                    var logReccord = new Log
                    {
                        Date = DateTime.Now,
                        MessageTipe = "fatal",
                        Operation = "StopService",
                        Exception = ex.GetType() + ": " + ex.Message
                    };
                    repository.Create(logReccord);
                }
            }
        }

        public void CheckJira()
        {
            using (var repository = new Repository<DbContext>()) //создаю репозиторий для работы с БД
            {
                while (true) //бесконечный цикл по проверке тикетов, переданных разработчикам
                {

                    var jira = new JiraClient(jiraParam.Value, userLoginParam.Value, userPasswordParam.Value); //объявляю клиент jira
                    repository.Create(new Log
                    {
                        Date = DateTime.Now,
                        MessageTipe = "info",
                        Operation = "Начал получать тикеты из фильтра \"" + filterParam.Value + " \"",
                        Exception = ""
                    });

                    IEnumerable<Issue> issues = null; //список тикетов в Escallation
                    int cnt = 0; //количество тикетов в Escallation
                    try
                    {
                        issues = jira.EnumerateIssuesByQuery(filterParam.Value, null, 0);
                        cnt = issues.Count();
                    }
                    catch (Exception ex)
                    {
                        repository.Create(new Log
                        {
                            Date = DateTime.Now,
                            MessageTipe = "info",
                            Operation = "Не удалось получить тикеты из JIra",
                            Exception = ex.Message
                        });
                        continue;  //прерываю щикл
                    }
                    
                    repository.Create(new Log
                    {
                        Date = DateTime.Now,
                        MessageTipe = "info",
                        Operation = "Получено тикетов " + cnt,
                        Exception = ""
                    });
                   

                    foreach (var issue in issues) //смотрю все тикеты
                    {
                        try
                        {
                            if (issue.fields.labels.Contains("watch"))
                                //если есть метка watch пишу в лог и перехожу к следующему тикету
                            {
                                var logReccord = new Log
                                {
                                    Date = DateTime.Now,
                                    MessageTipe = "info",
                                    Operation = "Тикет " + issue.key + " уже имеет метку watch",
                                    Exception = ""
                                };
                                repository.Create(logReccord);
                                continue; //прерываю цикл foreach
                            }

                            var links = issue.fields.issuelinks; //здесь уже метки watch у тикета нет   
                            if (links != null)
                            {
                                foreach (var link in links)
                                {
                                    var inwardIssue = link.inwardIssue;
                                    var outwardIssue = link.outwardIssue;
                                    int countOfLinks = links.Count; //суммарное количество ссылок
                                    int countOfClosedLinks = 0; //количество ссылок на закрытые тикеты

                                    if (inwardIssue.id != null)
                                    {
                                        var ticket = jira.LoadIssue(inwardIssue);
                                        if (ticket.fields.status.name == "Закрыто")
                                        {
                                            countOfClosedLinks++;
                                        }
                                    }
                                    if (outwardIssue.id != null)
                                    {
                                        var ticket = jira.LoadIssue(outwardIssue);
                                        if (ticket.fields.status.name == "Закрыто")
                                        {
                                            countOfClosedLinks++;
                                        }
                                    }
                                    if (countOfLinks == countOfClosedLinks)
                                    {
                                        issue.fields.labels.Add("watch");
                                        jira.CreateComment(issue, "Все связанные тикеты на команду разработки были закрыты." + Environment.NewLine + "Необходимо убедиться, что проблема решена и закрыть данный тикет, сообщив пользователю, когда исправление будет в релизе.",
                                            new Visibility {type = "role", value = "Service Desk Team"});
                                        jira.UpdateIssue(issue);
                                        repository.Create(new Log
                                        {
                                            Date = DateTime.Now,
                                            MessageTipe = "info",
                                            Operation = "Тикету " + issue.key + " добавлена метка watch",
                                            Exception = ""
                                        });
                                    }
                                    else
                                    {
                                        repository.Create(new Log
                                        {
                                            Date = DateTime.Now,
                                            MessageTipe = "info",
                                            Operation = "Тикет " + issue.key + " связан с открытым тиктетом",
                                            Exception = ""
                                        });
                                    }
                                }
                            }
                            Thread.Sleep(1000); //ждём секунду, чтобы не перенапряч jira 
                        }
                        catch (Exception e)
                        {
                            repository.Create(new Log
                            {
                                Date = DateTime.Now,
                                MessageTipe = "error",
                                Operation = "Ошибка при обработке тикета " + issue.key,
                                Exception = e.Message
                            });
                        }
                    }
                    Thread.Sleep(10000); //ждём 10 секунд, прежде чем снова начать проверять все тикеты в Escalation
                }
            }
        } 

    }
}