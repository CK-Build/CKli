using FluentAssertions;
using NUnit.Framework;
using System;

namespace CK.Env.DependencyModel.Tests
{
    public class ProjectAutoNamingTests
    {
        [Test]
        public void homonym_projects_are_automatically_named_inside_the_same_solution_when_added()
        {
            var ctx = new SolutionContext();
            var s = ctx.AddSolution( "/Solution/Path", "SolutionName" );

            var pC = s.AddProject( "Dir1", ".Net", "Homonym" );
            pC.Name.Should().Be( "Homonym" );
            s.Invoking( _ => _.AddProject( "Dir1", ".Net", "Homonym" ) ).Should().Throw<InvalidOperationException>();
            pC.Name.Should().Be( "Homonym" );

            var pJ = s.AddProject( "Dir1", "Node", "Homonym" );
            pC.Name.Should().Be( "(.Net)Homonym" );
            pJ.Name.Should().Be( "(Node)Homonym" );

            var pJa = s.AddProject( "AltDir", "Node", "Homonym" );
            pC.Name.Should().Be( "(.Net)Homonym" );
            pJ.Name.Should().Be( "(Node)Dir1/Homonym" );
            pJa.Name.Should().Be( "(Node)AltDir/Homonym" );

            var pCa = s.AddProject( "AltDir", ".Net", "Homonym" );
            pC.Name.Should().Be( "(.Net)Dir1/Homonym" );
            pCa.Name.Should().Be( "(.Net)AltDir/Homonym" );
            pJ.Name.Should().Be( "(Node)Dir1/Homonym" );
            pJa.Name.Should().Be( "(Node)AltDir/Homonym" );
        }

        [Test]
        public void homonym_projects_inside_the_same_solution_are_simplified_when_removed()
        {
            var ctx = new SolutionContext();
            var s = ctx.AddSolution( "/Solution/Path", "SolutionName" );

            var pC = s.AddProject( "Dir1", ".Net", "Homonym" );
            var pJ = s.AddProject( "Dir1", "Node", "Homonym" );
            var pJa = s.AddProject( "AltDir", "Node", "Homonym" );
            var pCa = s.AddProject( "AltDir", ".Net", "Homonym" );

            pC.Name.Should().Be( "(.Net)Dir1/Homonym" );
            pCa.Name.Should().Be( "(.Net)AltDir/Homonym" );
            pJ.Name.Should().Be( "(Node)Dir1/Homonym" );
            pJa.Name.Should().Be( "(Node)AltDir/Homonym" );

            s.RemoveProject( pC );
            pCa.Name.Should().Be( "(.Net)Homonym" );
            pJ.Name.Should().Be( "(Node)Dir1/Homonym" );
            pJa.Name.Should().Be( "(Node)AltDir/Homonym" );

            s.RemoveProject( pJa );
            pCa.Name.Should().Be( "(.Net)Homonym" );
            pJ.Name.Should().Be( "(Node)Homonym" );

        }

        [Test]
        public void homonym_projects_accross_solutions_are_prefixed_with_Solution_name()
        {
            var ctx = new SolutionContext();
            var s1 = ctx.AddSolution( "/S1/Path", "S1" );
            var s2 = ctx.AddSolution( "/S2/Path", "S2" );

            var p1s1 = s1.AddProject( "DirP1", "c#", "P1" );
            p1s1.Name.Should().Be( "P1" );

            var p1s2 = s2.AddProject( "DirP1", "c#", "P1" );
            p1s1.Name.Should().Be( "S1|P1" );
            p1s2.Name.Should().Be( "S2|P1" );

            s1.RemoveProject( p1s1 );
            p1s2.Name.Should().Be( "P1" );

        }
    }
}
