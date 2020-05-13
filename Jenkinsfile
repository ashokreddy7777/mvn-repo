node('Linux')
{
  def maven = docker.image ('maven:latest')
  maven.pull() // make sure the latest available from Docker Hub
  maven.inside
  {
    git 'https://github.com/ashokreddy7777/mvn-repo.git'
    sh 'mvn -B -X clean install'
  }
}