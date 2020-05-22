pipeline{
  agent{label 'lin'}
  options{timeout (time: 1, unit:'HOURS')}
  tools{
    maven 'maven'
    jdk 'java'
  }
  stages{
    stage('Build'){
      steps{
        sh '''
            echo "PATH = ${PATH}"
            echo "M2_HOME = ${M2_HOME}"
            mvn -X clean install
        '''     
      }
    }
    stage('Tomcat Deploy'){
      steps{
        deploy adapters: [tomcat9(credentialsId: 'agent_linux', path: '', url: 'http://34.201.67.144:8080')], contextPath: null, war: '**/*.war'
      }
    }
    stage('ws cleanup'){
      steps{
        cleanWs()
      }
    }
  }
}
