﻿// -----------------------------------------------------------------------
//  <copyright file="SeanBuchanan.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Abstractions.Data;
using Raven.Client;
using Raven.Client.Indexes;
using Raven.Json.Linq;
using Xunit;

namespace Raven.Tests.MailingList
{
	public class SeanBuchanan : RavenTest
	{
		public class Consultant : INamedDocument
		{
			public int Id { get; set; }
			public string Name { get; set; }
			public int YearsOfService { get; set; }
		}

		public interface INamedDocument
		{
			int Id { get; set; }
			
			string Name { get; set; }
		}

		public class Skill : INamedDocument
		{
			public int Id { get; set; }
			public string Name { get; set; }
		}

		public class Proficiency
		{
			public int Id { get; set; }
			public DenormalizedReference<Consultant> Consultant { get; set; }
			public DenormalizedReference<Skill> Skill { get; set; }
			public string SkillLevel { get; set; }
		}

		public class DenormalizedReference<T> where T : INamedDocument
		{
			public int Id { get; set; }
			public string Name { get; set; }

			public static implicit operator DenormalizedReference<T>(T doc)
			{
				return new DenormalizedReference<T>
				{
					Id = doc.Id,
					Name = doc.Name
				};
			}
		}

		public class Proficiencies_ConsultantId : AbstractIndexCreationTask<Proficiency>
		{
			public Proficiencies_ConsultantId()
			{
				Map = proficiencies => proficiencies.Select(proficiency => new {Consultant_Id = proficiency.Consultant.Id});
			}
		}

		[Fact]
		public void FactMethodName()
		{
			using (var store = NewDocumentStore())
			{
				IndexCreation.CreateIndexes(typeof (Proficiencies_ConsultantId).Assembly, store);

				//Write the test data to the database.
				using (IDocumentSession session = store.OpenSession())
				{
					Skill skill1 = new Skill {Id = 1, Name = "C#"};
					Skill skill2 = new Skill {Id = 2, Name = "SQL"};
					Consultant consultant1 = new Consultant {Id = 1, Name = "Subha", YearsOfService = 6};
					Consultant consultant2 = new Consultant {Id = 2, Name = "Tom", YearsOfService = 5};
					Proficiency proficiency1 = new Proficiency
					{
						Id = 1,
						Consultant = consultant1,
						Skill = skill1,
						SkillLevel = "Expert"
					};

					session.Store(skill1);
					session.Store(skill2);
					session.Store(consultant1);
					session.Store(consultant2);
					session.Store(proficiency1);
					session.Store(proficiency2);
					session.Store(proficiency3);
					session.SaveChanges();

					//Block1
					{
						List<Proficiency> proficiencies = session.Query<Proficiency>("Proficiencies/ConsultantId")
						                                         .Customize(o => o.WaitForNonStaleResultsAsOfLastWrite())
						                                         .Where(o => o.Consultant.Id == 1).ToList();
						foreach (Proficiency proficiency in proficiencies)
						{
							Console.WriteLine("Id: " + proficiency.Id + " ConsultantName: " + proficiency.Consultant.Name);
						}
					}
				}


				using (IDocumentSession session = store.OpenSession())
				{
					Consultant consultant1 = session.Load<Consultant>(1);

					//Here I am changing the name of one consultant from "Subha" to "Subhashini".
					//A denormalized reference to this name exists in the Proficiency class. After this update, I will need to sync the denormalized reference.
					//As I am changing a Consultant document, I would not expect my index to need updating because the index is on the Proficiency collection.
					consultant1.Name = "Subhashini";
					session.SaveChanges();

					//Block1
					//This block of code simply lists the names of the consultants in the Proficiencies collection. Since I have not synced the collection
					//yet, I expect the consultant name to still be "Subha."
					{
						List<Proficiency> list = session.Query<Proficiency>("Proficiencies/ConsultantId")
							//.Customize(o => o.WaitForNonStaleResultsAsOfLastWrite())
						                                .Where(o => o.Consultant.Id == 1).ToList();
						foreach (Proficiency proficiency in list)
						{
							Console.WriteLine("Id: " + proficiency.Id + " ConsultantName: " + proficiency.Consultant.Name);
						}
					}
				}

				//I use this patch to update the consultant name to "Subha" in the Proficiencies collection.
				store.DatabaseCommands.UpdateByIndex("Proficiencies/ConsultantId",
					new IndexQuery
					{
						Query = "Consultant_Id:1"
					},
					new[]
					{
						new PatchRequest
						{
							Type = PatchCommandType.Modify,
							Name = "Consultant",
							Nested = new[]
							{
								new PatchRequest
								{
									Type = PatchCommandType.Set,
									Name = "Name",
									Value = new RavenJValue("Subhashini")
								}
							}
						}
					},
					allowStale: false);

				//Here, I again list the name of the consultant in the Proficiencies collection and expect it to be "Subhashini".
				using (IDocumentSession session = store.OpenSession())
				{
					//Block2
					{
						List<Proficiency> proficiencies = session.Query<Proficiency>("Proficiencies/ConsultantId")
						                                         .Customize(o => o.WaitForNonStaleResultsAsOfLastWrite())
						                                         .Where(o => o.Consultant.Id == 1).ToList();
						foreach (Proficiency proficiency in proficiencies)
						{
							Console.WriteLine("Id: " + proficiency.Id + " ConsultantName: " + proficiency.Consultant.Name);
						}
					}
				}
			}
		}
	}
}