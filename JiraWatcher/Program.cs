﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Topshelf;
using JiraApiOpenSourseLibrary.JiraRestClient;
using System.Threading.Tasks;

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
                    s.ConstructUsing(name => new Prog());  //создаём службу из класса Prog
                    s.WhenStarted(tc => tc.Start());   //говорим, какой метод будет при старте службы
                    s.WhenStopped(tc => tc.Stop());  //говорим, какой метод выполнится при остановке службы
                });
                x.RunAsNetworkService();  //указываем свойства службы
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
                    using (var repository = new Repository<DbContext>())  //использую репозиторий для работы с БД, какая будет БД указано в DbContext
                    {
                        repository.Create(new Log   //создаю объект Log и пишу его в БД
                        {
                            Date = DateTime.Now,
                            MessageTipe = "error",
                            Operation = "StartService",
                            Exception = ex.GetType() + ": " + ex.Message
                        });
                        repository.Create(new Log
                        {
                            Date = DateTime.Now,
                            MessageTipe = "info",
                            Operation = "StartService2Attemp",
                            Exception = ""
                        });
                    }

                }

                using (var repository = new Repository<DbContext>()) //инициализирую парамтры приложения из БД
                {
                    jiraParam = repository.Get<Parametr>(p => p.Name == "jira");
                    userLoginParam = repository.Get<Parametr>(p => p.Name == "dafaultuserlogin");
                    userPasswordParam = repository.Get<Parametr>(p => p.Name == "dafaultuserpassword");
                    filterParam = repository.Get<Parametr>(p => p.Name == "Filter");
                }
                Task.WhenAll(CheckJira()); //метод в бесконечном цикле будет проверять jira 

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
                    repository.Create(new Log
                    {
                        Date = DateTime.Now,
                        MessageTipe = "info",
                        Operation = "StopService",
                        Exception = "",
                    });
                }
                Thread.ResetAbort();
            }
            catch (Exception ex)
            {
                using (var repository = new Repository<DbContext>())
                {
                    repository.Create(new Log
                    {
                        Date = DateTime.Now,
                        MessageTipe = "fatal",
                        Operation = "StopService",
                        Exception = ex.GetType() + ": " + ex.Message
                    });
                }
            }
        }

        async Task CheckJira()
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
                        issues = jira.EnumerateIssuesByQuery(filterParam.Value, null, 0).ToList();
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
                            //здесь уже метки watch у тикета нет
                            var links = issue.fields.issuelinks;    //смотрю, есть ли линки у тикета
                            if (links != null)
                            {
                                int countOfLinks = links.Count; //суммарное количество ссылок
                                int countOfClosedLinks = 0; //количество ссылок на закрытые тикеты

                                foreach (var link in links)
                                {
                                    var inwardIssue = link.inwardIssue;  //есть входящие и исходящие связи в тикете
                                    var outwardIssue = link.outwardIssue;

                                    if (link.inwardIssue.id != null)  //считаем входящие связи с закрытыми тикетами
                                    {
                                        var ticket = jira.LoadIssue(inwardIssue);
                                        if (((ticket.fields.status.name == "Закрыто" || ticket.fields.status.name == "Решено")&issue.fields.assignee.displayName!= "technologsupport")
                                            | ((ticket.fields.status.name == "Закрыто") & issue.fields.assignee.displayName == "technologsupport"))
                                        {
                                            countOfClosedLinks++;
                                        }
                                    }
                                    if (link.outwardIssue.id != null)  //считаем исходящие связи с закрытыми тикетами
                                    {
                                        var ticket = jira.LoadIssue(outwardIssue);
                                        if (((ticket.fields.status.name == "Закрыто" || ticket.fields.status.name == "Решено") & issue.fields.assignee.displayName != "technologsupport")
                                            | ((ticket.fields.status.name == "Закрыто") & issue.fields.assignee.displayName == "technologsupport"))
                                        {
                                            countOfClosedLinks++;
                                        }
                                    }
                                    if (countOfLinks == countOfClosedLinks)  //смотрим что суммарное количество линков равно сумме линков к решёнными и закрытым тикетам
                                    {
                                        issue.fields.labels.Add("watch");
                                        jira.CreateComment(issue, "Все связанные тикеты на команду разработки были решены или закрыты." + Environment.NewLine + "Необходимо убедиться, что проблема решена и закрыть данный тикет, сообщив пользователю, когда исправление будет в релизе.",
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
                            await Task.Delay(1000); //ждём секунду, чтобы не перенапряч jira 
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
                    await Task.Delay(10000); //ждём 10 секунд, прежде чем снова начать проверять все тикеты в Escalation
                }
            }
        } 

    }
}