node('Linux')
{
  stage "Docker Build"
  def maven = docker.image ('maven:latest')
  maven.pull() // make sure the latest available from Docker Hub
  maven.inside
  {
    git 'https://github.com/ashokreddy7777/mvn-repo.git'
    sh 'mvn -B -X clean install'
  }
  stage "Jira"
  post {
    always {
      jiraSendBuildInfo branch: '', site: 'avrb.atlassian.net'
    }
  }
}